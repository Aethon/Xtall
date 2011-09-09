using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml;

namespace XtallLib
{
    public class XtallManifest
    {
        public string MenuPath { get; private set; }
        public string ProductName { get; private set; }
        public string Branding { get; private set; }

        public XmlElement RunInfo { get; private set; }
        public string Md5Hash { get; private set; }

        public IList<XtallFileInfo> Files { get; private set; }
        public XtallFileInfo Boot { get; private set; }

        public XtallManifest(IEnumerable<XtallFileInfo> files, string boot, string productName, string menuPath = null, string branding = null, XmlElement runInfo = null)
        {
            if (boot == null)
                throw new ArgumentNullException("boot");
            if (files == null)
                throw new ArgumentNullException("files");
            if (productName == null)
                throw new ArgumentNullException("productName");

            ProductName = productName;
            MenuPath = menuPath;
            Branding = branding;
            RunInfo = runInfo;
            Files = new ReadOnlyCollection<XtallFileInfo>(new List<XtallFileInfo>(files));
            Md5Hash = ManifestManager.GetListHash(files.Select(x => x.Filename).OrderBy(x => x));

            Boot = files.SingleOrDefault(x => 0 == string.Compare(x.Filename, boot));
            if (Boot == null)
                throw new ArgumentException("boot path does not exist in the manifest");
        }
    }
}