using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;

namespace XtallLib
{
    public static class ManifestManager
    {
        #region Load

        public static XtallManifest Load(string manifestXml)
        {
            if (manifestXml == null)
                throw new ArgumentNullException("manifestXml");

            var doc = new XmlDocument();
            doc.LoadXml(manifestXml);

            var man = (XmlElement) doc.SelectSingleNode("/XtallManifest");
            if (man == null)
                throw new ApplicationException("Document is missing the XtallManifest element");
            var boot = GetRequiredAttribute(man, "Boot");
            var productName = GetRequiredAttribute(man, "ProductName");

            var installedDisplayName = man.GetAttribute("InstalledDisplayName");
            var runInfo = (XmlElement)man.SelectSingleNode("/RunInfo");
            
            var files = man.SelectNodes("File")
                .OfType<XmlElement>()
                .Select(LoadFileInfo);

            return new XtallManifest(files, boot, productName, installedDisplayName, runInfo);
        }

        private static XtallFileInfo LoadFileInfo(XmlElement element)
        {
            var filename = GetRequiredAttribute(element, "Filename");
            var md5Hash = GetRequiredAttribute(element, "Md5Hash");
            var byteCount = long.Parse(GetRequiredAttribute(element, "ByteCount"));

            return new XtallFileInfo(filename, md5Hash, byteCount);
        }

        private static string GetRequiredAttribute(XmlElement element, string name)
        {
            if (element == null)
                throw new ArgumentException("element");
            if (name == null)
                throw new ArgumentException("name");
            var attr = element.Attributes[name];
            if (attr == null)
                throw new ApplicationException(string.Format("Element '{0}' is missing required attribute '{1}'", element.LocalName, name));
            return attr.Value;
        }

        #endregion

        #region Prepare

        public static XtallManifest Prepare(string sourceFolder, string bootPath, string productName, string installedDisplayName = null, XmlElement runInfo = null, string ignore = null)
        {
            var ignoreRegex = ignore == null ? null
                : new Regex("^" + string.Join("|", ignore.Split(new [] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => "(" + Regex.Escape(x).Replace(@"\*", ".*").Replace(@"\?", ".") + ")")) + "$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return new XtallManifest(
                Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                .Select(x => PrepareFileInfo(sourceFolder, x, ignoreRegex)).Where(x => x != null),
                bootPath, productName, installedDisplayName, runInfo);
        }
        
        private static XtallFileInfo PrepareFileInfo(string fromFolder, string filename, Regex ignoreRegex)
        {
            var localPath = filename;
            if (0 == filename.IndexOf(fromFolder, StringComparison.InvariantCultureIgnoreCase))
                localPath = filename.Substring(fromFolder.Length);
            if (localPath.StartsWith(@"\"))
                localPath = localPath.Substring(1);

            if (ignoreRegex != null && ignoreRegex.IsMatch(localPath))
                return null;

            return new XtallFileInfo(localPath, GetFileChecksum(filename), new FileInfo(filename).Length);
        }

        #endregion

        public static void Write(XmlWriter writer, XtallManifest manifest)
        {
            if (writer == null)
                throw new ArgumentException("writer");
            if (manifest == null)
                throw new ArgumentException("manifest");

            writer.WriteStartElement("XtallManifest");
            {
                writer.WriteAttributeString("Boot", manifest.Boot.Filename);
                writer.WriteAttributeString("ProductName", manifest.ProductName);

                if (manifest.InstalledDisplayName != null)
                    writer.WriteAttributeString("InstalledDisplayName", manifest.InstalledDisplayName);

                writer.WriteStartElement("RunInfo");
                if (manifest.RunInfo != null)
                    writer.WriteRaw(manifest.RunInfo.OuterXml);
                writer.WriteEndElement();

                foreach (var x in manifest.Files)
                {
                    writer.WriteStartElement("File");
                    {
                        writer.WriteAttributeString("Filename", x.Filename);
                        writer.WriteAttributeString("Md5Hash", x.Md5Hash);
                        writer.WriteAttributeString("ByteCount", x.ByteCount.ToString());
                    }
                    writer.WriteEndElement();
                }
            }
            writer.WriteEndElement();
        }

        public static string GetFileChecksum(string localPath)
        {
            if (localPath == null)
                throw new ArgumentNullException("localPath");

            string result;
            try
            {
                using (var md5 = new MD5CryptoServiceProvider())
                {
                    using (var f = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.ReadWrite, 8192))
                        md5.ComputeHash(f);
                    result = string.Join("", md5.Hash.Select(x => string.Format("{0:X2}", x)).ToArray());
                }
            }
            catch (Exception x)
            {
                throw new ApplicationException(string.Format("Could not checksum file '{0}': {1}", localPath, x.Message), x);
            }
            return result;
        }

        public static string GetListHash(IEnumerable<string> hashes)
        {
            if (hashes == null)
                throw new ArgumentNullException("hashes");

            string result;
            try
            {
                using (var s = new StreamWriter(new MemoryStream()))
                {
                    foreach (var hash in hashes)
                        s.Write(hash);
                    s.Flush();
                    var bs = s.BaseStream;
                    bs.Position = 0;
                    using (var md5 = new MD5CryptoServiceProvider())
                    {
                        md5.ComputeHash(bs);
                        result = string.Join("", md5.Hash.Select(x => string.Format("{0:X2}", x)).ToArray());
                    }
                }
            }
            catch (Exception x)
            {
                throw new ApplicationException(string.Format("Could not hash the list of {0} strings: {1}", hashes.Count(), x.Message), x);
            }
            return result;
        }
    }
}
