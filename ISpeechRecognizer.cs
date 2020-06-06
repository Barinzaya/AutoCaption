using AutoSub;
using System;

namespace AutoCaption
{
    public interface ISpeechRecognizer : IDisposable
    {
        event EventHandler SpeechCancelled;
        event EventHandler<string> SpeechCompleted;
        event EventHandler<string> SpeechPartial;

        void Start(RecognitionConfig config);
        void Stop();
    }
}
