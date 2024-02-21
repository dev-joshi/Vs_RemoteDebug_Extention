namespace RemoteDebug.WinRM
{
    using Microsoft.Management.Infrastructure;
    using System;
    using System.Threading.Tasks;

    internal class CimMethodObserver : IObserver<CimMethodResult>
    {
        private readonly Func<CimMethodResult, Task> onSuccess;
        private readonly Func<Exception, Task> onFailure;

        public CimMethodObserver(Func<CimMethodResult, Task> onSuccess, Func<Exception, Task> onFailure)
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

        public void OnNext(CimMethodResult value)
        {
            _ = this.onSuccess(value);
        }
    }
}
