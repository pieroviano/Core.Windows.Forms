<%@ WebService Language="C#" Class="WebFormsSample.Calc" %>
using System;
using System.Web.Services;

namespace WebFormsSample
{
    [WebService(Namespace = "http://webformsport.example/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1, EmitConformanceClaims = true)]
    public class Calc : WebService
    {
        [WebMethod]
        public int Add (int a, int b) { return a + b; }

        [WebMethod]
        public string Echo (string text) { return "echo: " + text; }
    }
}
