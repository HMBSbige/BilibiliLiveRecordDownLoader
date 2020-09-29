using ReactiveUI;
using System;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    public class MyReactiveObject : ReactiveObject
    {
        [JsonIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changing => base.Changing;

        [JsonIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changed => base.Changed;

        [JsonIgnore]
        public new IObservable<Exception> ThrownExceptions => base.ThrownExceptions;
    }
}
