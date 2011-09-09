﻿using System;
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

            var man = (XmlElement) doc.SelectSingleNode("/AlloyManifest");
            if (man == null)
                throw new ApplicationException("Document is missing the AlloyManifest element");
            var boot = GetRequiredAttribute(man, "Boot");
            var productName = GetRequiredAttribute(man, "ProductName");

            var menuPath = man.GetAttribute("MenuPath");
            var branding = man.GetAttribute("Branding");
            var runInfo = (XmlElement)man.SelectSingleNode("/RunInfo");
            
            var files = man.SelectNodes("File")
                .OfType<XmlElement>()
                .Select(LoadFileInfo);

            return new XtallManifest(files, boot, productName, menuPath, branding, runInfo);
        }

        private static XtallFileInfo LoadFileInfo(XmlElement element)
        {
            var filename = GetRequiredAttribute(element, "Filename");
            var md5Hash = GetRequiredAttribute(element, "Md5Hash");

            return new XtallFileInfo(filename, md5Hash);
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

        public static XtallManifest Prepare(string sourceFolder, string bootPath, string productName, string menuPath = null, string branding = null, XmlElement runInfo = null, string ignore = null)
        {
            var ignoreRegex = ignore == null ? null
                : new Regex("^" + Regex.Escape(ignore).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return new XtallManifest( 
                Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                .Select(x => PrepareFileInfo(sourceFolder, x, ignoreRegex)).Where(x => x != null),
                bootPath, productName, menuPath, branding, runInfo);
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

            return new XtallFileInfo(localPath, GetFileChecksum(filename));
        }

        #endregion

        public static void Write(XmlWriter writer, XtallManifest manifest)
        {
            if (writer == null)
                throw new ArgumentException("writer");
            if (manifest == null)
                throw new ArgumentException("manifest");

            writer.WriteStartElement("AlloyManifest");
            {
                writer.WriteAttributeString("Boot", manifest.Boot.Filename);
                writer.WriteAttributeString("ProductName", manifest.ProductName);

                if (manifest.MenuPath != null)
                    writer.WriteAttributeString("MenuPath", manifest.MenuPath);
                if (manifest.Branding != null)
                    writer.WriteAttributeString("Branding", manifest.Branding);
                if (manifest.RunInfo != null)
                    writer.WriteRaw(manifest.RunInfo.OuterXml);

                foreach (var x in manifest.Files)
                {
                    writer.WriteStartElement("File");
                    {
                        writer.WriteAttributeString("Filename", x.Filename);
                        writer.WriteAttributeString("Md5Hash", x.Md5Hash);
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
                    using (var f = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192))
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
