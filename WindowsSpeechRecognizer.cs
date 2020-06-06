using AutoSub;
using System;
using System.Speech.Recognition;

namespace AutoCaption
{
    public class WindowsSpeechRecognizer : ISpeechRecognizer
    {
        public event EventHandler SpeechCancelled;
        public event EventHandler<string> SpeechCompleted;
        public event EventHandler<string> SpeechPartial;

        private RecognitionConfig _config;
        private SpeechRecognitionEngine _speechEngine;

        private bool _recognizing;
        private bool _speaking;

        public void Start(RecognitionConfig config)
        {
            _config = config;

            if(_speechEngine == null)
            {
                _speechEngine = new SpeechRecognitionEngine();
                _speechEngine.LoadGrammar(new DictationGrammar());

                _speechEngine.SpeechHypothesized += OnSpeechHypothesized;
                _speechEngine.SpeechRecognized += OnSpeechRecognized;

                _speechEngine.SetInputToDefaultAudioDevice();
            }

            if(!_recognizing)
            {
                _recognizing = true;
                _speaking = false;
                _speechEngine.RecognizeAsync(RecognizeMode.Multiple);
            }
        }

        private void OnSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            var threshold = _speaking ? _config.MinUpdateConfidence : _config.MinStartConfidence;
            if(e.Result.Confidence >= threshold)
            {
                _speaking = true;
                SpeechPartial(this, e.Result.Text);
            }
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            var threshold = _speaking ? _config.MinKeepConfidence : _config.MinStartConfidence;

            var keep = e.Result.Confidence >= threshold;
            if(keep == _speaking)
            {
                _speaking = false;
                if(keep)
                {
                    SpeechCompleted(this, e.Result.Text);
                }
                else
                {
                    SpeechCancelled(this, EventArgs.Empty);
                }
            }
        }

        public void Stop()
        {
            if(_speaking)
            {
                _speaking = false;
                SpeechCancelled(this, EventArgs.Empty);
            }

            if(_recognizing)
            {
                _speechEngine.RecognizeAsyncCancel();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                _speechEngine?.Dispose();
                _speechEngine = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
