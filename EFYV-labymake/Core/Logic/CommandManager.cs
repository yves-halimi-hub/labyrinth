using System;
using System.Collections.Generic;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public interface ICommand
    {
        void Execute();
        void Undo();
    }

    public interface ISizedCommand : ICommand
    {
        long EstimatedBytes { get; }
    }

    public readonly struct CommandHistorySnapshot
    {
        public int UndoCount { get; }
        public int RedoCount { get; }
        public long UndoBytes { get; }
        public long RedoBytes { get; }
        public bool CanUndo => UndoCount > Config.Command.EmptyStackCount;
        public bool CanRedo => RedoCount > Config.Command.EmptyStackCount;

        internal CommandHistorySnapshot(int undoCount, int redoCount, long undoBytes, long redoBytes)
        {
            UndoCount = undoCount;
            RedoCount = redoCount;
            UndoBytes = undoBytes;
            RedoBytes = redoBytes;
        }
    }

    public sealed class CommandManager
    {
        private readonly LinkedList<ICommand> undoHistory = new LinkedList<ICommand>();
        private readonly LinkedList<ICommand> redoHistory = new LinkedList<ICommand>();
        private readonly int capacity;
        private readonly long byteCapacity;
        private long undoBytes;
        private long redoBytes;

        public event Action<CommandHistorySnapshot> HistoryChanged;

        public bool CanUndo => undoHistory.Count > Config.Command.EmptyStackCount;
        public bool CanRedo => redoHistory.Count > Config.Command.EmptyStackCount;
        public CommandHistorySnapshot Current => new CommandHistorySnapshot(
            undoHistory.Count,
            redoHistory.Count,
            undoBytes,
            redoBytes);

        public CommandManager(
            int capacity = Config.Command.DefaultHistoryCapacity,
            long byteCapacity = Config.Command.DefaultHistoryByteCapacity)
        {
            if (capacity <= Config.Command.EmptyStackCount) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (byteCapacity <= Config.Command.EmptyStackCount) throw new ArgumentOutOfRangeException(nameof(byteCapacity));
            this.capacity = capacity;
            this.byteCapacity = byteCapacity;
        }

        public void ExecuteCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            command.Execute();
            RecordExecutedCommand(command);
        }

        public void RecordExecutedCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            long commandBytes = GetEstimatedBytes(command);
            redoHistory.Clear();
            redoBytes = Config.Common.EmptyCount;
            if (commandBytes > byteCapacity)
            {
                undoHistory.Clear();
                undoBytes = Config.Common.EmptyCount;
                Publish();
                return;
            }

            while (undoHistory.Count >= capacity || undoBytes > byteCapacity - commandBytes)
            {
                undoBytes -= GetEstimatedBytes(undoHistory.First.Value);
                undoHistory.RemoveFirst();
            }
            undoHistory.AddLast(command);
            undoBytes += commandBytes;
            Publish();
        }

        public bool Undo()
        {
            if (!CanUndo) return false;

            ICommand command = undoHistory.Last.Value;
            undoHistory.RemoveLast();
            long commandBytes = GetEstimatedBytes(command);
            undoBytes -= commandBytes;
            command.Undo();
            redoHistory.AddLast(command);
            redoBytes += commandBytes;
            Publish();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;

            ICommand command = redoHistory.Last.Value;
            redoHistory.RemoveLast();
            long commandBytes = GetEstimatedBytes(command);
            redoBytes -= commandBytes;
            command.Execute();
            undoHistory.AddLast(command);
            undoBytes += commandBytes;
            Publish();
            return true;
        }

        public void Clear()
        {
            if (!CanUndo && !CanRedo) return;
            undoHistory.Clear();
            redoHistory.Clear();
            undoBytes = Config.Common.EmptyCount;
            redoBytes = Config.Common.EmptyCount;
            Publish();
        }

        private void Publish()
        {
            HistoryChanged?.Invoke(Current);
        }

        private static long GetEstimatedBytes(ICommand command)
        {
            var sized = command as ISizedCommand;
            long estimatedBytes = sized?.EstimatedBytes ?? Config.Command.EstimatedCommandOverheadBytes;
            return estimatedBytes <= Config.Command.EmptyStackCount
                ? Config.Command.EstimatedCommandOverheadBytes
                : estimatedBytes;
        }
    }
}
