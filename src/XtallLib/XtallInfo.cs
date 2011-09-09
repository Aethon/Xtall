using System.Collections.Generic;
using System.Linq;

namespace XtallLib
{
    public class XtallInfo
    {
        // the display name of the installed program
        public readonly string Name;

        // the visual path (e.g., menu path) of the installed program
        public readonly string VisualPath;

        // path to the manifest on the host
        public readonly string ManifestPath;

        // the list of org ids that the program will accept manifests from
        public readonly IEnumerable<string> TrustedOrgIds;

        public XtallInfo(string name, string visualPath, string manifestPath = null, IEnumerable<string> trustedOrgIds = null)
        {
            Name = name;
            TrustedOrgIds = trustedOrgIds != null ? trustedOrgIds.ToArray() : null;
            ManifestPath = manifestPath;
            VisualPath = visualPath;
        }
    }
}