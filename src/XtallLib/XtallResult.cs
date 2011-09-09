namespace XtallLib
{
    public class XtallResult
    {
        public readonly bool Failed;
        public readonly bool ShouldContinue;
        public readonly int ExecResult;
        public readonly string Log;
        public readonly XtallManifest Manifest;
        public readonly CodeCacheManager CacheManager;

        public XtallResult(bool shouldContinue, bool failed, int execResult, string log, XtallManifest manifest, CodeCacheManager cacheManager)
        {
            ShouldContinue = shouldContinue;
            Failed = failed;
            CacheManager = cacheManager;
            Manifest = manifest;
            Log = log;
            ExecResult = execResult;
        }
    }
}