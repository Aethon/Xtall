using System;
using System.Threading;
using System.Xml;

namespace Xtall
{
    public interface IXtallObserver
    {
        void SetRunInfo(XmlElement info);
        void SetMessage(string message);
        void Error(Exception exception, string action, string log);
        void Dismiss(bool proceeding);
    }
}