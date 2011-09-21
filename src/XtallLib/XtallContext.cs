using System.Xml;

namespace XtallLib
{
    public interface IXtallContext
    {
        string Url { get; }
        XtallManifest Manifest { get; }
    }

    // TODO: INotifyPropertyChanged
    internal class XtallContext : IXtallContext
    {
        public string Url { get; set; }
        public XtallManifest Manifest { get; set; }
    }
}
