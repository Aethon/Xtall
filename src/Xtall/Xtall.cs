using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using Ionic.Zlib;
using XtallLib;
using CompressionMode = Ionic.Zlib.CompressionMode;
using GZipStream = Ionic.Zlib.GZipStream;

namespace Xtall
{
    class XtallProgram
    {
        // USAGE: preparealloyassets -source: file_source_folder -boot: boot_file_path [-passenger: passenger_file_path] [-pfx: pfx_file_path] [-ignore: ignore_regex] [-gzip] [-icon: icon_file_path] [-product: name] [-menupath: path] output_folder
        static void Main(string[] args)
        {
            try
            {
                Log("Preparing manifest with args '{0}'", string.Join(" ", args));

                var options = new Options(args);

                if (options.Loose.Count != 1)
                    throw new ArgumentException("Must specify exactly one output folder.");
                var outputPath = options.Loose[0];
                if (!options.Keyed.ContainsKey("source:"))
                    throw new ArgumentException("Must specify the file source folder (-source: file_source_folder).");
                if (!options.Keyed.ContainsKey("boot:"))
                    throw new ArgumentException("Must specify the boot file (-boot: boot_file_path).");
                var gzip = options.Keyed.ContainsKey("gzip");

                // prepare manifest using the source folder
                var sourceFolder = options.Keyed["source:"];
                Log("Preparing manifest using source folder '{0}'", sourceFolder);
                var prepared = ManifestManager.Prepare(sourceFolder, options.Keyed["boot:"], options.Keyed["product:"], options.Keyed["menupath:"], ignore: options.Keyed["ignore:"]);

                // write manifest output
                Directory.CreateDirectory(outputPath);
                var manifestPath = Path.Combine(outputPath, "manifest.xml");
                Log("Writing output '{0}'", manifestPath);
                var writer = XmlWriter.Create(manifestPath);
                writer.WriteStartDocument();
                ManifestManager.Write(writer, prepared);
                writer.WriteEndDocument();
                writer.Close();

                // move and prepare all files...
                var rawFolder = Path.Combine(outputPath, "raw");
                CleanFolder("uncompressed", rawFolder);

                var gzipFolder = Path.Combine(outputPath, "gzip");
                CleanFolder("gzip", gzipFolder);

                foreach (var file in prepared.Files)
                {
                    var sourcePath = Path.Combine(sourceFolder, file.Filename);
                    var rawPath = Path.Combine(rawFolder, file.Filename);
                    var gzipPath = Path.Combine(gzipFolder, file.Filename + ".gzip");

                    using (var sourcefile = File.OpenRead(sourcePath))
                    {
                        Log("Copying '{0}' to '{1}'", sourcePath, rawPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(rawPath));
                        using (var rawfile = File.Open(rawPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                            sourcefile.CopyTo(rawfile);
                        sourcefile.Position = 0;

                        if (gzip)
                        {
                            Log("GZipping '{0}' to '{1}'", sourcePath, gzipPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(gzipPath));
                            using (var gzipped = File.Open(gzipPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                            using (var compressor = new GZipStream(gzipped, CompressionMode.Compress, CompressionLevel.BestCompression))
                                sourcefile.CopyTo(compressor);
                        }
                    }
                }

                // prepare the shuttle
                using (var shuttleStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Xtall.XtallShuttle.exe"))
                {
                    var setup = Path.Combine(outputPath, "setup.exe");
                    using (var setupStream = File.Create(setup))
                    {
                        shuttleStream.CopyTo(setupStream);
                    }

                    string passengerFile;
                    if (options.Keyed.ContainsKey("passenger:"))
                        passengerFile = options.Keyed["passenger:"];
                    else
                        passengerFile = options.Keyed["boot:"];

                    var passenger = File.ReadAllBytes(Path.Combine(sourceFolder, passengerFile));
                    var pin = GCHandle.Alloc(passenger, GCHandleType.Pinned);

                    var handle = BeginUpdateResource(setup, false);
                    // TODO test and feedback

                    UpdateResource(handle, "EXE", "IDR_PASSENGER", 0, pin.AddrOfPinnedObject(), (uint) passenger.Length);

                    EndUpdateResource(handle, false);

                    using (var s = new FileStream(setup, FileMode.Append, FileAccess.Write))
                    using (var bw = new BinaryWriter(s))
                    {
                        const int luggageSpace = 1024;
                        bw.Write(new byte[luggageSpace]);
                        bw.Write((int)0x42000042); // parameter space signature
                        bw.Write((int) 0);
                        bw.Write((int) luggageSpace + Marshal.SizeOf(luggageSpace)*3);
                    }
                }
            }
            catch (Exception x)
            {
                Console.Error.WriteLine("error: {0}\r\n{1}", x.Message, x);
                Environment.ExitCode = 1;
            }
        }

        private static void CleanFolder(string desc, string path)
        {
            Log("Cleaning {0} folder '{1}'", desc, path);
            try
            {
                Directory.Delete(path, true);
            }
            catch (DirectoryNotFoundException)
            {
                // this is OK; it's pretty much what we were trying to accomplish, anyway.
            }
            Directory.CreateDirectory(path);
        }

        private static void Log(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        [DllImport("kernel32")]
        private extern static bool UpdateResource(IntPtr hUpdate, string lpType, string lpName, ushort wLanguage, IntPtr lpData, uint cbData);

        [DllImport("kernel32")]
        private extern static IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32")]
        private extern static bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
    }
}
