using System;
using System.IO;
using System.Threading;

namespace AutoCaption
{
    public class PipeStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => -1;

        public override long Position
        {
            get => 0;
            set {}
        }

        private readonly byte[] _buffer;
        private int _read, _write;
        private bool _open;

        public PipeStream(int bufferSize)
        {
            if(bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException("Buffer size must be greater than 0.");
            }

            bufferSize--;
            bufferSize |= bufferSize >>  1;
            bufferSize |= bufferSize >>  2;
            bufferSize |= bufferSize >>  4;
            bufferSize |= bufferSize >>  8;
            bufferSize |= bufferSize >> 16;
            bufferSize++;

            _buffer = new byte[(uint)bufferSize];
            _open = true;
        }

        public override void Flush() {}
        public override long Seek(long offset, SeekOrigin origin) => -1;
        public override void SetLength(long value) {}

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock(_buffer)
            {
                var length = _buffer.Length;
                var total = 0;

                while(count > 0)
                {
                    var available = _write - _read;
                    if(available <= 0)
                    {
                        Monitor.Wait(_buffer);
                        available = _write - _read;
                    }

                    if(!_open)
                    {
                        break;
                    }

                    var copy = Math.Min(available, count);

                    var first = _read % length;
                    if(first + copy <= length)
                    {
                        Buffer.BlockCopy(_buffer, first, buffer, offset, copy);
                    }
                    else
                    {
                        var a = length - first;
                        var b = copy - a;
                        Buffer.BlockCopy(_buffer, first, buffer, offset, a);
                        Buffer.BlockCopy(_buffer, 0, buffer, offset + a, b);
                    }

                    _read += copy;
                    count -= copy;
                    offset += copy;
                    total += copy;
                    Monitor.Pulse(_buffer);
                }

                return total;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock(_buffer)
            {
                var length = _buffer.Length;

                while(count > 0)
                {
                    var free = length - (_write - _read);
                    if(free <= 0)
                    {
                        Monitor.Wait(_buffer);
                        free = length - (_write - _read);
                    }

                    if(!_open)
                    {
                        break;
                    }

                    var copy = Math.Min(count, free);

                    var first = _write % length;
                    if(first + copy <= length)
                    {
                        Buffer.BlockCopy(buffer, offset, _buffer, first, copy);
                    }
                    else
                    {
                        var a = length - first;
                        var b = copy - a;
                        Buffer.BlockCopy(buffer, offset, _buffer, first, a);
                        Buffer.BlockCopy(buffer, offset + a, _buffer, 0, b);
                    }

                    count -= copy;
                    offset += copy;
                    _write += copy;

                    Monitor.Pulse(_buffer);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            lock(_buffer)
            {
                _open = false;
                Monitor.PulseAll(_buffer);
            }
        }
    }
}
