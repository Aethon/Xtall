using System;
using System.IO;

namespace XtallLib
{
    public interface IXtallEnvironment
    {
        void LogAction(string format, params object[] args);
        void LogStatus(string format, params object[] args);
        LogState GetCurrentState();

        void GetResource(string url, Stream destination, Func<string, bool> contentTypePredicate = null);
        bool FileExists(string filename);
    }
}
