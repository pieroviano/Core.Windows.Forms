using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace WebApiSample.Controllers
{
	public class Widget
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public decimal Price { get; set; }
	}

	// A plain Web API controller. Action selection is by HTTP verb and route, and the return value is
	// content-negotiated into JSON or XML by the media-type formatters.
	public class WidgetsController : ApiController
	{
		static readonly List<Widget> widgets = new List<Widget> {
			new Widget { Id = 1, Name = "sprocket", Price = 9.99m },
			new Widget { Id = 2, Name = "flange",   Price = 24.50m },
			new Widget { Id = 3, Name = "grommet",  Price = 3.75m },
		};

		public IEnumerable<Widget> Get ()
		{
			return widgets;
		}

		public Widget Get (int id)
		{
			Widget widget = widgets.FirstOrDefault (w => w.Id == id);
			if (widget == null)
				// This Web API version's HttpResponseException takes a message, not a status code;
				// the status-code overload arrived in Web API 2.
				throw new HttpResponseException (Request.CreateResponse (HttpStatusCode.NotFound));

			return widget;
		}

		// Model binding from a JSON or XML request body.
		public HttpResponseMessage Post (Widget widget)
		{
			if (widget == null || string.IsNullOrEmpty (widget.Name))
				return Request.CreateResponse (HttpStatusCode.BadRequest);

			widget.Id = widgets.Max (w => w.Id) + 1;
			widgets.Add (widget);

			return Request.CreateResponse (HttpStatusCode.Created, widget);
		}
	}
}
