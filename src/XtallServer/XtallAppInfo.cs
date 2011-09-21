using System;
using System.Web;
using System.Xml;

namespace XtallServer
{
    public class XtallAppInfo
    {
        public string VirtualPath { get; set; }
        public string AssetPath { get; set; }
        public string CachePath { get; set; }

        public string SetupName { get; set; }
        public string InstalledDisplayName { get; set; }
        public XmlElement RunInfo { get; set; }

        public Func<HttpRequest, string, string> ReturnUrlSelector { get; set; }
        public Func<HttpRequest, string, string> ParameterSelector { get; set; }
    }
}