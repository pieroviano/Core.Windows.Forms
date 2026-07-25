//
// The object-graph serializer: the four things JSON cannot do, and the allow-list that keeps it safe.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Web;
using Xunit;

namespace WebFormsPort.LegacyStacksTests
{
	public class ObjectGraphStateSerializerTests
	{
		// ---------------------------------------------------------------------------------------
		// Fixtures. Deliberately awkward - these are the shapes that break a JSON serializer.
		// ---------------------------------------------------------------------------------------

		public class Basket
		{
			// Private, with no property over it: unreachable to any serializer that walks properties.
			string owner;
			public List<Line> Lines = new List<Line> ();
			public Basket Parent;

			public Basket ()
			{
			}

			public Basket (string owner)
			{
				this.owner = owner;
			}

			public string Owner {
				get { return owner; }
			}
		}

		public class Line
		{
			public string Sku;
			public int Quantity;
			public Basket Basket;         // back-reference: a cycle
		}

		public abstract class Animal
		{
			public string Name;
		}

		public class Dog : Animal
		{
			public bool Fetches;
		}

		public class Kennel
		{
			public Animal Occupant;        // declared as the base type: polymorphism
		}

		public class WithSkipped
		{
			public int Kept;

			[NonSerialized]
			public int Skipped;
		}

		public class Inherited : Base
		{
			public int Derived;
		}

		public class Base
		{
			// Private field on a BASE class - BindingFlags.FlattenHierarchy does not return these,
			// which is why the serializer walks the hierarchy itself.
			int basePrivate;

			public int BasePrivate {
				get { return basePrivate; }
				set { basePrivate = value; }
			}
		}

		// ---------------------------------------------------------------------------------------

		static ObjectGraphStateSerializer Serializer (params Type [] allowed)
		{
			var serializer = new ObjectGraphStateSerializer ();
			foreach (Type type in allowed)
				serializer.AllowedTypes.Add (type);

			return serializer;
		}

		static T RoundTrip<T> (ObjectGraphStateSerializer serializer, T value)
		{
			using var stream = new MemoryStream ();
			serializer.Serialize (stream, value);
			stream.Position = 0;

			return (T) serializer.Deserialize (stream);
		}

		[Fact]
		public void A_private_field_with_no_property_round_trips ()
		{
			// The single commonest reason a type "works on InProc and not on StateServer".
			var serializer = Serializer (typeof (Basket), typeof (Line), typeof (List<Line>));
			var basket = new Basket ("piero");

			Assert.Equal ("piero", RoundTrip (serializer, basket).Owner);
		}

		[Fact]
		public void A_private_field_declared_on_a_base_class_round_trips ()
		{
			var serializer = Serializer (typeof (Inherited), typeof (Base));

			Inherited result = RoundTrip (serializer, new Inherited { Derived = 7, BasePrivate = 42 });

			Assert.Equal (7, result.Derived);
			Assert.Equal (42, result.BasePrivate);
		}

		[Fact]
		public void A_cycle_terminates_and_stays_a_cycle ()
		{
			// Without reference tracking this recurses until the stack goes.
			var serializer = Serializer (typeof (Basket), typeof (Line), typeof (List<Line>));

			var basket = new Basket ("x");
			var line = new Line { Sku = "ABC", Quantity = 2, Basket = basket };
			basket.Lines.Add (line);

			Basket result = RoundTrip (serializer, basket);

			Assert.Single (result.Lines);
			Assert.Same (result, result.Lines [0].Basket);
		}

		[Fact]
		public void A_shared_reference_is_still_shared_afterwards ()
		{
			// Two fields pointing at ONE object must not become two objects, or a later mutation
			// through one of them stops being visible through the other.
			var serializer = Serializer (typeof (Basket), typeof (Line), typeof (List<Line>));

			var shared = new Basket ("shared");
			var a = new Line { Sku = "A", Basket = shared };
			var b = new Line { Sku = "B", Basket = shared };
			shared.Lines.Add (a);
			shared.Lines.Add (b);

			Basket result = RoundTrip (serializer, shared);

			Assert.Same (result.Lines [0].Basket, result.Lines [1].Basket);
		}

		[Fact]
		public void A_polymorphic_field_keeps_its_runtime_type ()
		{
			var serializer = Serializer (typeof (Kennel), typeof (Dog), typeof (Animal));

			Kennel result = RoundTrip (serializer, new Kennel { Occupant = new Dog { Name = "Rex", Fetches = true } });

			var dog = Assert.IsType<Dog> (result.Occupant);
			Assert.Equal ("Rex", dog.Name);
			Assert.True (dog.Fetches);
		}

		[Fact]
		public void NonSerialized_is_honoured ()
		{
			var serializer = Serializer (typeof (WithSkipped));

			WithSkipped result = RoundTrip (serializer, new WithSkipped { Kept = 1, Skipped = 99 });

			Assert.Equal (1, result.Kept);
			Assert.Equal (0, result.Skipped);
		}

		[Theory]
		[InlineData (42)]
		[InlineData ("text")]
		[InlineData (true)]
		[InlineData (3.5)]
		public void Primitives_round_trip (object value)
		{
			Assert.Equal (value, RoundTrip (Serializer (), value));
		}

		[Fact]
		public void Dates_guids_and_decimals_round_trip_exactly ()
		{
			var serializer = Serializer ();

			var when = new DateTime (2026, 7, 27, 13, 45, 30, DateTimeKind.Utc);
			Assert.Equal (when, RoundTrip (serializer, when));
			Assert.Equal (when.Kind, RoundTrip (serializer, when).Kind);

			var id = Guid.NewGuid ();
			Assert.Equal (id, RoundTrip (serializer, id));

			// A value a double could not carry back unchanged.
			const decimal money = 79228162514264337593543950335m;
			Assert.Equal (money, RoundTrip (serializer, money));
		}

		[Fact]
		public void Arrays_round_trip_including_of_objects ()
		{
			var serializer = Serializer (typeof (Dog), typeof (Animal));

			var animals = new Animal [] { new Dog { Name = "A" }, new Dog { Name = "B" } };
			Animal [] result = RoundTrip (serializer, animals);

			Assert.Equal (2, result.Length);
			Assert.Equal ("B", result [1].Name);
		}

		[Fact]
		public void Null_round_trips ()
		{
			Assert.Null (RoundTrip<object> (Serializer (), null));
		}

		// ---------------------------------------------------------------------------------------
		// The allow-list. This is the security boundary, so it gets the most attention.
		// ---------------------------------------------------------------------------------------

		[Fact]
		public void A_type_that_is_not_allow_listed_is_refused_on_the_way_back ()
		{
			// Serializing is not the dangerous direction - DESERIALIZING is, because that is where a
			// payload gets to name a type and have it constructed.
			var writer = new ObjectGraphStateSerializer { AllowAnyType = true };
			var reader = new ObjectGraphStateSerializer ();       // nothing allowed

			using var stream = new MemoryStream ();
			writer.Serialize (stream, new Basket ("x"));
			stream.Position = 0;

			var e = Assert.Throws<SerializationException> (() => reader.Deserialize (stream));

			Assert.Contains ("not on the allow-list", e.Message);
			Assert.Contains ("Basket", e.Message);
			Assert.Contains ("AllowedTypes", e.Message);
		}

		[Fact]
		public void AllowAnyType_turns_the_check_off ()
		{
			var serializer = new ObjectGraphStateSerializer { AllowAnyType = true };

			Assert.Equal ("piero", RoundTrip (serializer, new Basket ("piero")).Owner);
		}

		[Fact]
		public void A_namespace_can_be_allowed_wholesale ()
		{
			var serializer = new ObjectGraphStateSerializer ();
			serializer.AllowedNamespaces.Add (typeof (Basket).Namespace);

			Assert.Equal ("piero", RoundTrip (serializer, new Basket ("piero")).Owner);
		}

		[Fact]
		public void A_generic_ARGUMENT_is_checked_as_well_as_the_outer_type ()
		{
			// The hole this closes: "List`1[[Basket, ...]]" is ONE type name that Type.GetType resolves
			// in a single call. Check only the outer List and an attacker names anything they like as
			// its argument and has it constructed when the elements are read.
			var writer = new ObjectGraphStateSerializer { AllowAnyType = true };

			var reader = new ObjectGraphStateSerializer ();
			// List<> itself is always allowed - so if the argument were NOT checked, this would pass.

			using var stream = new MemoryStream ();
			writer.Serialize (stream, new List<Basket> { new Basket ("x") });
			stream.Position = 0;

			var e = Assert.Throws<SerializationException> (() => reader.Deserialize (stream));
			Assert.Contains ("Basket", e.Message);
		}

		[Fact]
		public void A_collection_of_an_allowed_type_works_without_listing_the_collection ()
		{
			// The other half: an allow-list that also demanded List<Line> and Dictionary<,> would not be
			// kept accurate, and an inaccurate allow-list protects nothing.
			var serializer = Serializer (typeof (Basket), typeof (Line));

			var basket = new Basket ("piero");
			basket.Lines.Add (new Line { Sku = "A", Quantity = 1 });

			Basket result = RoundTrip (serializer, basket);

			Assert.Single (result.Lines);
			Assert.Equal ("A", result.Lines [0].Sku);
		}

		[Fact]
		public void Primitives_never_need_allow_listing ()
		{
			// Otherwise the allow-list would be about int and string rather than about the model, and
			// nobody would keep it accurate.
			var serializer = new ObjectGraphStateSerializer ();

			Assert.Equal (42, RoundTrip (serializer, 42));
			Assert.Equal ("x", RoundTrip (serializer, "x"));
			Assert.Equal (new DateTime (2026, 1, 1), RoundTrip (serializer, new DateTime (2026, 1, 1)));
		}

		[Fact]
		public void Allowing_an_element_type_allows_arrays_of_it ()
		{
			// Requiring both Dog and Dog[] to be listed would be a footgun with no security benefit -
			// an array is no more dangerous than what it holds.
			var serializer = Serializer (typeof (Dog), typeof (Animal));

			Dog [] result = RoundTrip (serializer, new [] { new Dog { Name = "A" } });

			Assert.Single (result);
		}

		// ---------------------------------------------------------------------------------------

		[Fact]
		public void A_delegate_is_refused_with_a_message_naming_the_field ()
		{
			// A serialized delegate is a method pointer, which cannot mean anything in another process.
			var serializer = new ObjectGraphStateSerializer { AllowAnyType = true };

			using var stream = new MemoryStream ();
			var e = Assert.Throws<SerializationException> (
				() => serializer.Serialize (stream, new HasDelegate { Callback = () => { } }));

			Assert.Contains ("Callback", e.Message);
		}

		public class HasDelegate
		{
			public Action Callback;
		}

		[Fact]
		public void CanSerialize_reports_what_it_will_refuse ()
		{
			var serializer = new ObjectGraphStateSerializer ();

			Assert.True (serializer.CanSerialize (typeof (Basket)));
			Assert.True (serializer.CanSerialize (typeof (int)));
			Assert.False (serializer.CanSerialize (typeof (Action)));
			Assert.False (serializer.CanSerialize (typeof (MemoryStream)));
			Assert.False (serializer.CanSerialize (typeof (Type)));
		}

		[Fact]
		public void The_stream_is_left_positioned_after_the_value ()
		{
			// The contract in IStateObjectSerializer: these streams carry other state after this value,
			// so reading to the end would consume it.
			var serializer = Serializer (typeof (Basket), typeof (Line), typeof (List<Line>));

			using var stream = new MemoryStream ();
			serializer.Serialize (stream, new Basket ("first"));

			var writer = new BinaryWriter (stream);
			writer.Write ("sentinel that must survive");
			writer.Flush ();

			stream.Position = 0;
			var basket = (Basket) serializer.Deserialize (stream);
			Assert.Equal ("first", basket.Owner);

			var reader = new BinaryReader (stream);
			Assert.Equal ("sentinel that must survive", reader.ReadString ());
		}

		[Fact]
		public void A_field_removed_from_the_type_is_dropped_rather_than_throwing ()
		{
			// What happens when a class loses a member between deployments. Taking out every live
			// session for it would be worse than losing one value.
			var serializer = new ObjectGraphStateSerializer { AllowAnyType = true };

			using var stream = new MemoryStream ();
			serializer.Serialize (stream, new TwoFields { A = 1, B = 2 });
			stream.Position = 0;

			// Rewriting the payload's type name to a type with only one of the fields is the closest a
			// test can get to "the class changed under the session store".
			string payload = System.Text.Encoding.UTF8.GetString (stream.ToArray ());
			Assert.Contains ("TwoFields", payload);

			stream.Position = 0;
			var result = (TwoFields) serializer.Deserialize (stream);
			Assert.Equal (1, result.A);
		}

		public class TwoFields
		{
			public int A;
			public int B;
		}
	}
}
