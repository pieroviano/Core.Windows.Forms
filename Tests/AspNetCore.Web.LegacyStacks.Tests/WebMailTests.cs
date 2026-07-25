//
// System.Web.Mail, against a real SMTP conversation.
//
// The point of porting this at all is that an application's existing SmtpMail.Send call sites keep
// working, so the test drives exactly those - and it drives them at a socket, because the only way to
// know the message was actually FORMED correctly is to read what went down the wire. A test that
// asserted on MailMessage's properties would pass with a completely broken transport.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mail;
using Xunit;

// System.Web.Mail (SmtpMail, MailMessage, MailAttachment, MailFormat, MailPriority) is obsolete by
// design - .NET redirects new code to System.Net.Mail. But this suite exists precisely to prove the
// legacy call sites keep working, so every reference here is deliberate and the obsoletion warning
// (CS0618) is noise for this file alone.
#pragma warning disable CS0618

namespace WebFormsPort.LegacyStacksTests
{
	public class WebMailTests
	{
		/// <summary>
		/// The smallest SMTP server that will satisfy a client: greet, accept every command, and hand
		/// back what it was told.
		/// </summary>
		sealed class FakeSmtpServer : IDisposable
		{
			readonly TcpListener listener;
			readonly Task accepting;

			public FakeSmtpServer ()
			{
				listener = new TcpListener (IPAddress.Loopback, 0);
				listener.Start ();

				Port = ((IPEndPoint) listener.LocalEndpoint).Port;
				accepting = Task.Run (Accept);
			}

			public int Port { get; }

			/// <summary>Everything the client sent, including the DATA body.</summary>
			public string Conversation { get; private set; } = "";

			public List<string> Recipients { get; } = new List<string> ();

			public string Sender { get; private set; }

			void Accept ()
			{
				try {
					using TcpClient client = listener.AcceptTcpClient ();
					using NetworkStream stream = client.GetStream ();
					using var reader = new StreamReader (stream, Encoding.ASCII);
					using var writer = new StreamWriter (stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

					writer.WriteLine ("220 localhost fake ESMTP");

					var transcript = new StringBuilder ();
					bool inData = false;
					string line;

					while ((line = reader.ReadLine ()) != null) {
						transcript.AppendLine (line);

						if (inData) {
							// "." alone ends DATA. This is the whole SMTP framing rule that matters.
							if (line == ".") {
								inData = false;
								writer.WriteLine ("250 Ok: queued");
							}

							continue;
						}

						if (line.StartsWith ("MAIL FROM", StringComparison.OrdinalIgnoreCase))
							Sender = Between (line, '<', '>');
						else if (line.StartsWith ("RCPT TO", StringComparison.OrdinalIgnoreCase))
							Recipients.Add (Between (line, '<', '>'));

						if (line.StartsWith ("DATA", StringComparison.OrdinalIgnoreCase)) {
							inData = true;
							writer.WriteLine ("354 End data with <CR><LF>.<CR><LF>");
							continue;
						}

						if (line.StartsWith ("QUIT", StringComparison.OrdinalIgnoreCase)) {
							writer.WriteLine ("221 Bye");
							break;
						}

						writer.WriteLine ("250 Ok");
					}

					Conversation = transcript.ToString ();
				} catch (Exception) {
					// The client disconnecting mid-conversation is how this ends when a test fails; it
					// must not surface as an unobserved task exception and mask the real assertion.
				}
			}

			static string Between (string text, char open, char close)
			{
				int start = text.IndexOf (open);
				int end = text.LastIndexOf (close);
				return start < 0 || end <= start ? text : text.Substring (start + 1, end - start - 1);
			}

			public void WaitForCompletion ()
			{
				accepting.Wait (TimeSpan.FromSeconds (10));
			}

			public void Dispose ()
			{
				try {
					listener.Stop ();
				} catch (Exception) {
				}
			}
		}

		/// <summary>
		/// Points a message at the fake server. The PORT is set through the CDO configuration field,
		/// which is how System.Web.Mail has always expressed it - SmtpServer is a host name and nothing
		/// else, so "host:port" is read as a host name containing a colon and fails to resolve.
		/// </summary>
		static MailMessage Addressed (FakeSmtpServer server, MailMessage message)
		{
			SmtpMail.SmtpServer = "127.0.0.1";
			message.Fields ["http://schemas.microsoft.com/cdo/configuration/smtpserverport"] =
				server.Port.ToString ();

			return message;
		}

		[Fact]
		public void SmtpMail_Send_delivers_through_a_real_smtp_conversation ()
		{
			using var server = new FakeSmtpServer ();

			SmtpMail.Send (Addressed (server, new MailMessage {
				From = "sender@example.com",
				To = "recipient@example.com",
				Subject = "A subject",
				Body = "A body",
			}));

			server.WaitForCompletion ();

			Assert.Equal ("sender@example.com", server.Sender);
			Assert.Contains ("recipient@example.com", server.Recipients);
			Assert.Contains ("A subject", server.Conversation);
			Assert.Contains ("A body", server.Conversation);
		}

		[Fact]
		public void The_four_argument_overload_still_exists_for_existing_call_sites ()
		{
			// SmtpMail.Send (from, to, subject, body) is what most legacy code actually calls. It cannot
			// carry a CDO port field, so it is not wire-testable against a fake server on a random port -
			// but its presence and signature are exactly what a ported application depends on.
			System.Reflection.MethodInfo send = typeof (SmtpMail).GetMethod (
				"Send", new [] { typeof (string), typeof (string), typeof (string), typeof (string) });

			Assert.NotNull (send);
			Assert.True (send.IsStatic);
		}

		[Fact]
		public void A_MailMessage_carries_its_headers_to_the_wire ()
		{
			using var server = new FakeSmtpServer ();

			SmtpMail.Send (Addressed (server, new MailMessage {
				From = "from@example.com",
				To = "to@example.com",
				Cc = "cc@example.com",
				Subject = "Ported",
				Body = "This came through System.Web.Mail.",
				BodyFormat = MailFormat.Text,
				Priority = MailPriority.High,
			}));

			server.WaitForCompletion ();

			Assert.Equal ("from@example.com", server.Sender);
			Assert.Contains ("to@example.com", server.Recipients);
			Assert.Contains ("cc@example.com", server.Recipients);

			Assert.Contains ("Subject: Ported", server.Conversation);
			Assert.Contains ("This came through System.Web.Mail.", server.Conversation);
		}

		[Fact]
		public void An_html_message_says_so_in_its_content_type ()
		{
			using var server = new FakeSmtpServer ();

			SmtpMail.Send (Addressed (server, new MailMessage {
				From = "from@example.com",
				To = "to@example.com",
				Subject = "HTML",
				Body = "<p>hello</p>",
				BodyFormat = MailFormat.Html,
			}));

			server.WaitForCompletion ();

			Assert.Contains ("text/html", server.Conversation);
			Assert.Contains ("<p>hello</p>", server.Conversation);
		}

		[Fact]
		public void The_types_are_in_their_original_namespace ()
		{
			// The whole reason to port this rather than tell people to rewrite: the call sites do not
			// change, which means the namespace must not either.
			Assert.Equal ("System.Web.Mail", typeof (MailMessage).Namespace);
			Assert.Equal ("System.Web.Mail", typeof (SmtpMail).Namespace);
			Assert.Equal ("System.Web.Mail", typeof (MailAttachment).Namespace);
			Assert.Equal ("System.Web.Mail", typeof (MailFormat).Namespace);
			Assert.Equal ("System.Web.Mail", typeof (MailPriority).Namespace);
		}

		[Fact]
		public void An_unreachable_server_fails_rather_than_silently_dropping_the_mail ()
		{
			// Nothing listens on port 1. Mail that vanishes without an error is the worst outcome here -
			// nobody finds out until the customer asks where their receipt is.
			SmtpMail.SmtpServer = "127.0.0.1";

			var message = new MailMessage {
				From = "a@example.com", To = "b@example.com", Subject = "s", Body = "b",
			};
			message.Fields ["http://schemas.microsoft.com/cdo/configuration/smtpserverport"] = "1";

			Assert.ThrowsAny<Exception> (() => SmtpMail.Send (message));
		}
	}
}
