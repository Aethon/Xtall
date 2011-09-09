using System;
using System.Threading;
using System.Xml;

namespace XtallLib
{
    internal class XtallObserverProxy : IDisposable
    {
        private readonly IXtallEnvironment _environment;

        private readonly Func<Action<IXtallObserver>, bool> _observerFactory;
        private IXtallObserver _observer;
        private readonly ManualResetEvent _observerReadyGate = new ManualResetEvent(false);
        private readonly ManualResetEvent _observerCompleteGate = new ManualResetEvent(false);
        
        private bool _proceed;

        public XtallObserverProxy(Func<Action<IXtallObserver>, bool> factory, IXtallEnvironment environment)
        {
            if (environment == null)
                throw new ArgumentNullException("environment");
            _environment = environment;

            _observerFactory = factory;

            _environment.LogAction("starting observer UI thread");

            var thread = new Thread(ObserverThread);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void ObserverThread()
        {
            _environment.LogStatus("entered observer UI thread");
            _environment.LogAction("calling observer factory");
            _proceed = _observerFactory(obs =>
                         {
                             _observer = obs;
                             _observerReadyGate.Set();
                         });
            _observerCompleteGate.Set();
            _environment.LogStatus("observer returned {0}", _proceed);
        }

        public void Dispose()
        {
            DismissAndWait(false);
        }

        public void SetRunInfo(XmlElement info)
        {
            _observerReadyGate.WaitOne();
            _observer.SetRunInfo(info);
        }

        public void SetMessage(string message)
        {
            _observerReadyGate.WaitOne();
            _observer.SetMessage(message);
        }

        public void Error(Exception exception, string action, string log)
        {
            _observerReadyGate.WaitOne();
            _observer.Error(exception, action, log);
        }

        public bool DismissAndWait(bool proceeding)
        {
            _observerReadyGate.WaitOne();
            _observer.Dismiss(proceeding);
            _observerCompleteGate.WaitOne();
            return _proceed;
        }
    }
}
