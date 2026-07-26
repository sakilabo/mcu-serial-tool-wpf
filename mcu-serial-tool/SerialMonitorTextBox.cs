using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MCUSerialTool
{
    public sealed class SerialMonitorTextBox : TextBox
    {
        private const int MaxDisplayLines = 500;
        private const int MaxControlSequenceLength = 64;

        private readonly StringBuilder _controlSequence = new StringBuilder();
        private readonly StringBuilder _currentLine = new StringBuilder();
        private AnsiParserState _ansiParserState;
        private int _currentLineCursor;

        private enum AnsiParserState
        {
            Text,
            Escape,
            ControlSequence
        }

        public SerialMonitorTextBox()
        {
            IsReadOnly = true;
            TextWrapping = TextWrapping.Wrap;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }

        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }

            var previousCurrentLineLength = _currentLine.Length;
            var completedLines = new StringBuilder();
            foreach (var character in text)
            {
                ProcessCharacter(character, completedLines);
            }

            var completedOutputLength = Math.Max(
                0,
                Text.Length - previousCurrentLineLength);
            Text =
                Text.Substring(0, completedOutputLength)
                + completedLines
                + _currentLine;
            TrimOutput();
            CaretIndex = Text.Length;
            ScrollToEnd();
        }

        public void ClearOutput()
        {
            Clear();
            _currentLine.Clear();
            _currentLineCursor = 0;
            _controlSequence.Clear();
            _ansiParserState = AnsiParserState.Text;
        }

        private void ProcessCharacter(
            char character,
            StringBuilder completedLines)
        {
            switch (_ansiParserState)
            {
                case AnsiParserState.Escape:
                    ProcessEscapeCharacter(character, completedLines);
                    return;
                case AnsiParserState.ControlSequence:
                    ProcessControlSequenceCharacter(character, completedLines);
                    return;
            }

            if (character == '\x1B')
            {
                _ansiParserState = AnsiParserState.Escape;
                return;
            }
            if (character == '\u009B')
            {
                BeginControlSequence();
                return;
            }
            if (character == '\r')
            {
                _currentLineCursor = 0;
                return;
            }
            if (character == '\n')
            {
                CompleteCurrentLine(completedLines);
                return;
            }
            if (character == '\b')
            {
                _currentLineCursor = Math.Max(0, _currentLineCursor - 1);
                return;
            }
            if (character == '\t')
            {
                _currentLineCursor = ((_currentLineCursor / 8) + 1) * 8;
                return;
            }
            if (char.IsControl(character)) { return; }

            WriteCurrentCharacter(character);
        }

        private void ProcessEscapeCharacter(
            char character,
            StringBuilder completedLines)
        {
            _ansiParserState = AnsiParserState.Text;
            if (character == '[')
            {
                BeginControlSequence();
                return;
            }
            if (character == '\x1B')
            {
                _ansiParserState = AnsiParserState.Escape;
                return;
            }
            if (character == 'D'
                || character == 'E'
                || character == 'M')
            {
                CompleteCurrentLine(completedLines);
            }
        }

        private void BeginControlSequence()
        {
            _controlSequence.Clear();
            _ansiParserState = AnsiParserState.ControlSequence;
        }

        private void ProcessControlSequenceCharacter(
            char character,
            StringBuilder completedLines)
        {
            if (character >= '@' && character <= '~')
            {
                ProcessControlSequence(
                    _controlSequence.ToString(),
                    character,
                    completedLines);
                _controlSequence.Clear();
                _ansiParserState = AnsiParserState.Text;
                return;
            }

            if (_controlSequence.Length >= MaxControlSequenceLength)
            {
                _controlSequence.Clear();
                _ansiParserState = AnsiParserState.Text;
                return;
            }

            _controlSequence.Append(character);
        }

        private void ProcessControlSequence(
            string parameters,
            char command,
            StringBuilder completedLines)
        {
            switch (command)
            {
                case 'A':
                case 'B':
                case 'E':
                case 'F':
                case 'e':
                    AddLineFeeds(
                        GetAnsiParameter(parameters, 0, 1),
                        completedLines);
                    break;
                case 'H':
                case 'f':
                    CompleteCurrentLine(completedLines);
                    MoveToColumn(GetAnsiParameter(parameters, 1, 1));
                    break;
                case 'd':
                    CompleteCurrentLine(completedLines);
                    break;
                case 'C':
                case 'a':
                    _currentLineCursor +=
                        GetAnsiParameter(parameters, 0, 1);
                    break;
                case 'D':
                    _currentLineCursor = Math.Max(
                        0,
                        _currentLineCursor
                            - GetAnsiParameter(parameters, 0, 1));
                    break;
                case 'G':
                case '`':
                    MoveToColumn(GetAnsiParameter(parameters, 0, 1));
                    break;
                case 'K':
                    EraseCurrentLine(
                        GetAnsiParameter(parameters, 0, 0));
                    break;
            }
        }

        private void AddLineFeeds(
            int count,
            StringBuilder completedLines)
        {
            for (var index = 0; index < count; index++)
            {
                CompleteCurrentLine(completedLines);
            }
        }

        private void CompleteCurrentLine(StringBuilder completedLines)
        {
            completedLines.Append(_currentLine);
            completedLines.Append(Environment.NewLine);
            _currentLine.Clear();
            _currentLineCursor = 0;
        }

        private void MoveToColumn(int column)
        {
            _currentLineCursor = Math.Max(0, column - 1);
        }

        private void WriteCurrentCharacter(char character)
        {
            while (_currentLine.Length < _currentLineCursor)
            {
                _currentLine.Append(' ');
            }

            if (_currentLineCursor < _currentLine.Length)
            {
                _currentLine[_currentLineCursor] = character;
            }
            else
            {
                _currentLine.Append(character);
            }
            _currentLineCursor++;
        }

        private void EraseCurrentLine(int mode)
        {
            if (mode == 2)
            {
                _currentLine.Clear();
                return;
            }
            if (mode == 1)
            {
                var lastIndex = Math.Min(
                    _currentLineCursor,
                    _currentLine.Length - 1);
                for (var index = 0; index <= lastIndex; index++)
                {
                    _currentLine[index] = ' ';
                }
                return;
            }

            if (_currentLineCursor < _currentLine.Length)
            {
                _currentLine.Remove(
                    _currentLineCursor,
                    _currentLine.Length - _currentLineCursor);
            }
        }

        private static int GetAnsiParameter(
            string parameters,
            int parameterIndex,
            int defaultValue)
        {
            var fields = parameters.Split(';');
            if (parameterIndex >= fields.Length) { return defaultValue; }

            var field = fields[parameterIndex];
            var firstDigit = 0;
            while (firstDigit < field.Length
                && !char.IsDigit(field[firstDigit]))
            {
                firstDigit++;
            }
            if (firstDigit == field.Length) { return defaultValue; }

            var end = firstDigit;
            while (end < field.Length && char.IsDigit(field[end]))
            {
                end++;
            }

            int value;
            if (!int.TryParse(
                field.Substring(firstDigit, end - firstDigit),
                out value))
            {
                return defaultValue;
            }
            return value == 0 && defaultValue > 0
                ? defaultValue
                : value;
        }

        private void TrimOutput()
        {
            UpdateLayout();
            var excessLineCount = LineCount - MaxDisplayLines;
            if (excessLineCount <= 0) { return; }

            var firstCharacterToKeep =
                GetCharacterIndexFromLineIndex(excessLineCount);
            if (firstCharacterToKeep <= 0) { return; }

            var currentLineStart = Text.Length - _currentLine.Length;
            if (firstCharacterToKeep > currentLineStart)
            {
                var removedCurrentLineLength = Math.Min(
                    _currentLine.Length,
                    firstCharacterToKeep - currentLineStart);
                _currentLine.Remove(0, removedCurrentLineLength);
                _currentLineCursor = Math.Max(
                    0,
                    _currentLineCursor - removedCurrentLineLength);
            }

            Text = Text.Substring(firstCharacterToKeep);
        }
    }
}
