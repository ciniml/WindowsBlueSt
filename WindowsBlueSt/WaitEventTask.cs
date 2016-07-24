using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace WindowsBlueSt
{
    public static class WaitEventTask
    {
        public static Task<TEventArgs> FromTypedEvent<TSender, TEventArgs>(
            Action<TypedEventHandler<TSender, TEventArgs>> addHandler,
            Action<TypedEventHandler<TSender, TEventArgs>> removeHandler,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TEventArgs>();
            var cancelRegistration = cancellationToken.Register(() => tcs.SetCanceled());
            TypedEventHandler<TSender, TEventArgs> handler = (sender, args) =>
            {
                tcs.SetResult(args);
            };
            tcs.Task.ContinueWith(eventArgs =>
            {
                cancelRegistration.Dispose();
                removeHandler(handler);
            }, TaskContinuationOptions.None);
            addHandler(handler);
            return tcs.Task;
        }
    }
}
