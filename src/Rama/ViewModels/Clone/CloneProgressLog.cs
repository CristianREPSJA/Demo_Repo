using System;
using System.Collections.Generic;

using Rama.Core.Clone;

namespace SourceGit.ViewModels
{
    // Receptor del log de Commands.Clone: separa el progreso de git del resto de lineas (errores).
    public sealed class CloneProgressLog(Action<GitProgress> onProgress) : Models.ICommandLog
    {
        private const int MaxKept = 6;
        private readonly object _lock = new();
        private readonly Queue<string> _lines = new();

        public void AppendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("$ git", StringComparison.Ordinal))
                return;

            var progress = GitProgressParser.Parse(line);
            if (progress != null)
            {
                onProgress(progress);
                return;
            }

            lock (_lock)
            {
                _lines.Enqueue(line.Trim());
                while (_lines.Count > MaxKept)
                    _lines.Dequeue();
            }
        }

        public string LastLines()
        {
            lock (_lock)
                return string.Join("\n", _lines);
        }
    }
}
