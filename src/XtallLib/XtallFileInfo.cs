using System;

namespace XtallLib
{
    public class XtallFileInfo
    {
        public string Filename { get; private set; }
        public string Md5Hash { get; private set; }

        public XtallFileInfo(string filename, string md5Hash)
        {
            if (filename == null)
                throw new ArgumentNullException("filename");
            if (md5Hash == null)
                throw new ArgumentNullException("md5Hash");

            Filename = filename;
            Md5Hash = md5Hash;
        }
    }
}