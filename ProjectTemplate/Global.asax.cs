using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace ProjectTemplate
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
            ProjectServices services = new ProjectServices();

            try
            {
                services.SendDigestIfDue();
            }
            catch (Exception)
            {
                // Do not prevent website from starting if digest fails.
            }

        }
    }
}
