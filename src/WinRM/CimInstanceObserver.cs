namespace RemoteDebug.WinRM
{
    using Microsoft.Management.Infrastructure;
    using System;
    using System.Threading.Tasks;

    internal class CimInstanceObserver : IObserver<CimInstance>
    {
        private readonly Func<CimInstance, Task> onSuccess;
        private readonly Func<Exception, Task> onFailure;

        public CimInstanceObserver(Func<CimInstance, Task> onSuccess, Func<Exception, Task> onFailure)
        {
            this.onSuccess = onSuccess;
            this.onFailure = onFailure;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            _ = this.onFailure(error);
        }

        public void OnNext(CimInstance value)
        {
            _ = this.onSuccess(value);
        }
    }
}
