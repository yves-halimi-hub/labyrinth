using System;
using System.Threading;
using System.Threading.Tasks;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.Models;
using EFYVLabyMake.Core.Persistence;
using EFYVLabyMake.Core.Tools;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public sealed class DesignerSessionSnapshot
    {
        public string ProjectName { get; }
        public bool IsDirty { get; }
        public long ChangeVersion { get; }
        public int AnimationIndex { get; }
        public int FrameIndex { get; }
        public DateTimeOffset? LastSavedAt { get; }
        public ProjectValidationResult Validation { get; }
        public CommandHistorySnapshot History { get; }
        public AutosaveSnapshot Autosave { get; }
        public LiveDebugSnapshot LiveDebug { get; }
        public Exception PersistenceException { get; }

        internal DesignerSessionSnapshot(
            string projectName,
            bool isDirty,
            long changeVersion,
            int animationIndex,
            int frameIndex,
            DateTimeOffset? lastSavedAt,
            ProjectValidationResult validation,
            CommandHistorySnapshot history,
            AutosaveSnapshot autosave,
            LiveDebugSnapshot liveDebug,
            Exception persistenceException)
        {
            ProjectName = projectName;
            IsDirty = isDirty;
            ChangeVersion = changeVersion;
            AnimationIndex = animationIndex;
            FrameIndex = frameIndex;
            LastSavedAt = lastSavedAt;
            Validation = validation;
            History = history;
            Autosave = autosave;
            LiveDebug = liveDebug;
            PersistenceException = persistenceException;
        }
    }

    public sealed class DesignerSession : IDisposable
    {
        private readonly object gate = new object();
        private readonly ToolbarAPI toolbar;
        private readonly ProjectValidator validator;
        private readonly ProjectPersistenceService persistence;
        private readonly AutosaveController autosave;
        private readonly LiveDebugController liveDebug;
        private readonly PreviewController preview;
        private readonly CommandManager history;
        private readonly IDebounceScheduler clock;
        private readonly SynchronizationContext eventContext;

        private Frame gestureFrame;
        private FrameEditCapture gestureBefore;
        private ITool gestureTool;
        private bool gestureActive;
        private bool disposed;
        private bool isDirty;
        private long changeVersion;
        private DateTimeOffset? lastSavedAt;
        private Exception persistenceException;
        private ProjectValidationResult validation;
        private int animationIndex = Config.Common.NotFoundIndex;
        private int frameIndex = Config.Common.NotFoundIndex;

        public event Action<DesignerSessionSnapshot> StateChanged;

        public EFYVProject Project { get; private set; }
        public string ProjectName { get; private set; }
        public ITool ActiveTool { get; set; }
        public bool AutosaveEnabled { get; set; } = Config.Persistence.DefaultAutosaveEnabled;
        public PreviewController Preview => preview;
        public LiveDebugController LiveDebug => liveDebug;
        public CommandManager History => history;
        public DesignerSessionSnapshot Current => CreateSnapshot();

        public Frame CurrentFrame
        {
            get
            {
                if (animationIndex < Config.Common.FirstIndex || animationIndex >= Project.Animations.Count)
                    return null;
                AnimationState animation = Project.Animations[animationIndex];
                return frameIndex >= Config.Common.FirstIndex && frameIndex < animation.Frames.Count
                    ? animation.Frames[frameIndex]
                    : null;
            }
        }

        public DesignerSession(
            string projectName,
            EFYVProject project,
            ToolbarAPI toolbar,
            ProjectValidator validator,
            ProjectPersistenceService persistence,
            AutosaveController autosave,
            LiveDebugController liveDebug,
            PreviewController preview,
            CommandManager history,
            IDebounceScheduler clock,
            SynchronizationContext eventContext = null)
        {
            if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException(nameof(projectName));
            Project = project ?? throw new ArgumentNullException(nameof(project));
            this.toolbar = toolbar ?? throw new ArgumentNullException(nameof(toolbar));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            this.autosave = autosave ?? throw new ArgumentNullException(nameof(autosave));
            this.liveDebug = liveDebug ?? throw new ArgumentNullException(nameof(liveDebug));
            this.preview = preview ?? throw new ArgumentNullException(nameof(preview));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.eventContext = eventContext;
            ProjectName = projectName;
            persistence.GetProjectPath(projectName);

            validation = validator.Validate(Project);
            SelectFirstFrame();
            autosave.StateChanged += HandleAutosaveChanged;
            liveDebug.StateChanged += HandleLiveDebugChanged;
            history.HistoryChanged += HandleHistoryChanged;
        }

        public static DesignerSession Create(
            string projectName,
            EFYVProject project,
            string projectDirectory)
        {
            var schema = new AssetSchemaService();
            var toolbar = new ToolbarAPI(schema);
            var validator = new ProjectValidator(schema);
            var persistence = new ProjectPersistenceService(projectDirectory, schema);
            var autosave = new AutosaveController(persistence);
            var liveDebug = new LiveDebugController(
                new EFYVLabyMake.Core.Export.ExportEngine(validator),
                validator);
            return new DesignerSession(
                projectName,
                project,
                toolbar,
                validator,
                persistence,
                autosave,
                liveDebug,
                new PreviewController(),
                new CommandManager(),
                new TaskDebounceScheduler(),
                SynchronizationContext.Current);
        }

        public bool SelectFrame(int selectedAnimationIndex, int selectedFrameIndex)
        {
            ThrowIfDisposed();
            if (gestureActive || selectedAnimationIndex < Config.Common.FirstIndex ||
                selectedAnimationIndex >= Project.Animations.Count) return false;
            AnimationState animation = Project.Animations[selectedAnimationIndex];
            if (selectedFrameIndex < Config.Common.FirstIndex || selectedFrameIndex >= animation.Frames.Count)
                return false;

            animationIndex = selectedAnimationIndex;
            frameIndex = selectedFrameIndex;
            Publish();
            return true;
        }

        public bool PointerDown(int x, int y)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame;
            if (gestureActive || ActiveTool == null || frame == null) return false;

            gestureFrame = frame;
            gestureTool = ActiveTool;
            gestureBefore = FrameEditCapture.Capture(frame, gestureTool);
            gestureActive = true;
            try
            {
                gestureTool.OnPointerDown(Project, frame, x, y);
                return true;
            }
            catch
            {
                RollbackGesture();
                ClearGesture();
                throw;
            }
        }

        public bool PointerDrag(int x, int y)
        {
            ThrowIfDisposed();
            if (!gestureActive) return false;
            try
            {
                gestureTool.OnPointerDrag(Project, gestureFrame, x, y);
                return true;
            }
            catch
            {
                RollbackGesture();
                ClearGesture();
                throw;
            }
        }

        public bool PointerUp(int x, int y)
        {
            ThrowIfDisposed();
            if (!gestureActive) return false;

            try
            {
                gestureTool.OnPointerUp(Project, gestureFrame, x, y);
                var command = new FrameEditCommand(gestureFrame, gestureBefore);
                if (command.HasChanges)
                {
                    history.RecordExecutedCommand(command);
                    MarkDirty();
                }
                return command.HasChanges;
            }
            catch
            {
                RollbackGesture();
                throw;
            }
            finally
            {
                ClearGesture();
            }
        }

        public void CancelGesture()
        {
            ThrowIfDisposed();
            if (!gestureActive) return;
            RollbackGesture();
            ClearGesture();
            Publish();
        }

        public PropertyEditResult SetProperty(string fieldName, object value)
        {
            ThrowIfDisposed();
            object previousValue;
            bool hadPreviousValue = Project.AssetProperties.TryGetValue(fieldName, out previousValue);
            PropertyEditResult result = toolbar.TrySetProperty(Project, fieldName, value);
            if (!result.Succeeded) return result;

            object nextValue = Project.AssetProperties[fieldName];
            if (hadPreviousValue && Equals(previousValue, nextValue)) return result;
            history.RecordExecutedCommand(new PropertyEditCommand(
                Project.AssetProperties,
                fieldName,
                hadPreviousValue,
                previousValue,
                nextValue));
            MarkDirty();
            return result;
        }

        public bool Undo()
        {
            ThrowIfDisposed();
            if (!history.Undo()) return false;
            MarkDirty();
            return true;
        }

        public bool Redo()
        {
            ThrowIfDisposed();
            if (!history.Redo()) return false;
            MarkDirty();
            return true;
        }

        public void AddAnimation(AnimationState animation)
        {
            ThrowIfDisposed();
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            int insertionIndex = Project.Animations.Count;
            Project.Animations.Add(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.Insert(insertionIndex, animation),
                () => Project.Animations.RemoveAt(insertionIndex),
                EstimateAnimationBytes(animation)));
            animationIndex = insertionIndex;
            frameIndex = animation.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
        }

        public AnimationState GenerateAnimation(MovingTool tool)
        {
            ThrowIfDisposed();
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            Frame baseFrame = CurrentFrame ?? throw new InvalidOperationException();
            AnimationState generated = tool.GenerateAnimation(baseFrame);
            int existingIndex = Project.Animations.FindIndex(
                animation => string.Equals(animation.StateName, generated.StateName, StringComparison.Ordinal));
            if (existingIndex == Config.Common.NotFoundIndex)
            {
                AddAnimation(generated);
                return generated;
            }

            AnimationState previous = Project.Animations[existingIndex];
            Project.Animations[existingIndex] = generated;
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations[existingIndex] = generated,
                () => Project.Animations[existingIndex] = previous,
                Math.Max(EstimateAnimationBytes(previous), EstimateAnimationBytes(generated))));
            animationIndex = existingIndex;
            frameIndex = generated.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
            return generated;
        }

        public AnimationState DuplicateAnimation(int index)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            AnimationState clone = Project.Animations[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            Project.Animations.Insert(insertionIndex, clone);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.Insert(insertionIndex, clone),
                () => Project.Animations.RemoveAt(insertionIndex),
                EstimateAnimationBytes(clone)));
            animationIndex = insertionIndex;
            frameIndex = clone.Frames.Count > Config.Common.EmptyCount
                ? Config.Common.FirstIndex
                : Config.Common.NotFoundIndex;
            MarkDirty();
            return clone;
        }

        public void RemoveAnimation(int index)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            AnimationState removed = Project.Animations[index];
            Project.Animations.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Project.Animations.RemoveAt(index),
                () => Project.Animations.Insert(index, removed),
                EstimateAnimationBytes(removed)));
            SelectFirstFrame();
            MarkDirty();
        }

        public void MoveAnimation(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(fromIndex);
            ValidateAnimationIndex(toIndex);
            if (fromIndex == toIndex) return;
            Move(Project.Animations, fromIndex, toIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Move(Project.Animations, fromIndex, toIndex),
                () => Move(Project.Animations, toIndex, fromIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            animationIndex = toIndex;
            MarkDirty();
        }

        public void RenameAnimation(int index, string name)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            AnimationState animation = Project.Animations[index];
            string previous = animation.StateName;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            animation.StateName = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.StateName = name,
                () => animation.StateName = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)(previous?.Length ?? Config.Common.EmptyCount) + name.Length) * sizeof(char)));
            MarkDirty();
        }

        public void SetAnimationFps(int index, int fps)
        {
            ThrowIfDisposed();
            ValidateAnimationIndex(index);
            if (fps <= Config.Common.EmptyCount) throw new ArgumentOutOfRangeException(nameof(fps));
            AnimationState animation = Project.Animations[index];
            int previous = animation.FPS;
            if (previous == fps) return;
            animation.FPS = fps;
            history.RecordExecutedCommand(new DelegateCommand(
                () => animation.FPS = fps,
                () => animation.FPS = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public Frame AddFrame()
        {
            ThrowIfDisposed();
            if (animationIndex < Config.Common.FirstIndex || animationIndex >= Project.Animations.Count)
                throw new InvalidOperationException();

            AnimationState animation = Project.Animations[animationIndex];
            var frame = new Frame(Project.CanvasWidth, Project.CanvasHeight, animation.Frames.Count);
            int insertionIndex = animation.Frames.Count;
            animation.Frames.Add(frame);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.Insert(insertionIndex, frame); NormalizeFrameIndices(animation); },
                () => { animation.Frames.RemoveAt(insertionIndex); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(frame)));
            frameIndex = insertionIndex;
            MarkDirty();
            return frame;
        }

        public Frame DuplicateFrame(int index)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, index);
            Frame clone = animation.Frames[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            clone.FrameIndex = insertionIndex;
            animation.Frames.Insert(insertionIndex, clone);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.Insert(insertionIndex, clone); NormalizeFrameIndices(animation); },
                () => { animation.Frames.RemoveAt(insertionIndex); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(clone)));
            frameIndex = insertionIndex;
            MarkDirty();
            return clone;
        }

        public void RemoveFrame(int index)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, index);
            Frame removed = animation.Frames[index];
            animation.Frames.RemoveAt(index);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { animation.Frames.RemoveAt(index); NormalizeFrameIndices(animation); },
                () => { animation.Frames.Insert(index, removed); NormalizeFrameIndices(animation); },
                EstimateFrameBytes(removed)));
            frameIndex = animation.Frames.Count == Config.Common.EmptyCount
                ? Config.Common.NotFoundIndex
                : Math.Min(index, animation.Frames.Count - 1);
            MarkDirty();
        }

        public void MoveFrame(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            AnimationState animation = GetSelectedAnimation();
            ValidateFrameIndex(animation, fromIndex);
            ValidateFrameIndex(animation, toIndex);
            if (fromIndex == toIndex) return;
            Move(animation.Frames, fromIndex, toIndex);
            NormalizeFrameIndices(animation);
            history.RecordExecutedCommand(new DelegateCommand(
                () => { Move(animation.Frames, fromIndex, toIndex); NormalizeFrameIndices(animation); },
                () => { Move(animation.Frames, toIndex, fromIndex); NormalizeFrameIndices(animation); },
                Config.Command.EstimatedCommandOverheadBytes));
            frameIndex = toIndex;
            MarkDirty();
        }

        public Layer AddLayer(string name)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            var layer = new Layer(name, frame.Width, frame.Height);
            int insertionIndex = frame.Layers.Count;
            frame.Layers.Add(layer);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.Insert(insertionIndex, layer),
                () => frame.Layers.RemoveAt(insertionIndex),
                EstimateLayerBytes(layer)));
            MarkDirty();
            return layer;
        }

        public Layer DuplicateLayer(int index)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            Layer clone = frame.Layers[index].Clone();
            int insertionIndex = index + EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep;
            frame.Layers.Insert(insertionIndex, clone);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.Insert(insertionIndex, clone),
                () => frame.Layers.RemoveAt(insertionIndex),
                EstimateLayerBytes(clone)));
            MarkDirty();
            return clone;
        }

        public void RemoveLayer(int index)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            if (frame.Layers.Count <= EFYVBackend.Core.Data.EFYVLabyrinthConfig.Shared.UnitStep)
                throw new InvalidOperationException();
            Layer removed = frame.Layers[index];
            frame.Layers.RemoveAt(index);
            history.RecordExecutedCommand(new DelegateCommand(
                () => frame.Layers.RemoveAt(index),
                () => frame.Layers.Insert(index, removed),
                EstimateLayerBytes(removed)));
            MarkDirty();
        }

        public void MoveLayer(int fromIndex, int toIndex)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, fromIndex);
            ValidateLayerIndex(frame, toIndex);
            if (fromIndex == toIndex) return;
            Move(frame.Layers, fromIndex, toIndex);
            history.RecordExecutedCommand(new DelegateCommand(
                () => Move(frame.Layers, fromIndex, toIndex),
                () => Move(frame.Layers, toIndex, fromIndex),
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void RenameLayer(int index, string name)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            Layer layer = frame.Layers[index];
            string previous = layer.Name;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;
            layer.Name = name;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.Name = name,
                () => layer.Name = previous,
                Config.Command.EstimatedCommandOverheadBytes +
                    ((long)(previous?.Length ?? Config.Common.EmptyCount) + name.Length) * sizeof(char)));
            MarkDirty();
        }

        public void SetLayerVisibility(int index, bool isVisible)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            Layer layer = frame.Layers[index];
            bool previous = layer.IsVisible;
            if (previous == isVisible) return;
            layer.IsVisible = isVisible;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.IsVisible = isVisible,
                () => layer.IsVisible = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void SetLayerOpacity(int index, float opacity)
        {
            ThrowIfDisposed();
            Frame frame = CurrentFrame ?? throw new InvalidOperationException();
            ValidateLayerIndex(frame, index);
            Layer layer = frame.Layers[index];
            float previous = layer.Opacity;
            layer.Opacity = opacity;
            float normalized = layer.Opacity;
            if (previous == normalized) return;
            history.RecordExecutedCommand(new DelegateCommand(
                () => layer.Opacity = normalized,
                () => layer.Opacity = previous,
                Config.Command.EstimatedCommandOverheadBytes));
            MarkDirty();
        }

        public void LoadPreview(int selectedAnimationIndex)
        {
            ThrowIfDisposed();
            ProjectValidationResult result = validator.Validate(Project);
            if (!result.IsValid) throw new ProjectValidationException(result);
            preview.Load(ProjectSnapshot.Capture(Project), selectedAnimationIndex);
        }

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ProjectPersistenceSnapshot snapshot = ProjectPersistenceSnapshot.Capture(Project);
            long savedVersion;
            lock (gate) savedVersion = changeVersion;

            try
            {
                await Task.Run(
                    () => persistence.SaveProject(ProjectName, snapshot, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                lock (gate)
                {
                    lastSavedAt = clock.UtcNow;
                    persistenceException = null;
                    if (changeVersion == savedVersion) isDirty = false;
                }
                if (!isDirty) persistence.DeleteAutosave(ProjectName);
            }
            catch (Exception exception)
            {
                lock (gate) persistenceException = exception;
                Publish();
                throw;
            }
            Publish();
        }

        public void ReloadFromDisk(bool preferAutosave)
        {
            ThrowIfDisposed();
            CancelGesture();
            autosave.Cancel();
            liveDebug.CancelPending();

            Project = preferAutosave && persistence.AutosaveExists(ProjectName)
                ? persistence.LoadAutosave(ProjectName)
                : persistence.LoadProject(ProjectName);
            history.Clear();
            changeVersion = Config.Common.EmptyCount;
            isDirty = false;
            persistenceException = null;
            validation = validator.Validate(Project);
            SelectFirstFrame();
            Publish();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            autosave.StateChanged -= HandleAutosaveChanged;
            liveDebug.StateChanged -= HandleLiveDebugChanged;
            history.HistoryChanged -= HandleHistoryChanged;
            autosave.Dispose();
            liveDebug.Dispose();
        }

        private void MarkDirty()
        {
            lock (gate)
            {
                isDirty = true;
                changeVersion++;
                validation = validator.Validate(Project);
            }
            if (AutosaveEnabled)
                autosave.Schedule(ProjectName, () => ProjectPersistenceSnapshot.Capture(Project));
            liveDebug.NotifyProjectChanged(() => Project);
            Publish();
        }

        private void RollbackGesture()
        {
            if (gestureFrame == null || gestureBefore == null) return;
            var rollback = new FrameEditCommand(gestureFrame, gestureBefore);
            rollback.Undo();
        }

        private void ClearGesture()
        {
            gestureFrame = null;
            gestureBefore = null;
            gestureTool = null;
            gestureActive = false;
        }

        private void SelectFirstFrame()
        {
            animationIndex = Config.Common.NotFoundIndex;
            frameIndex = Config.Common.NotFoundIndex;
            for (int index = Config.Common.FirstIndex; index < Project.Animations.Count; index++)
            {
                if (Project.Animations[index]?.Frames.Count > Config.Common.EmptyCount)
                {
                    animationIndex = index;
                    frameIndex = Config.Common.FirstIndex;
                    return;
                }
            }
        }

        private DesignerSessionSnapshot CreateSnapshot()
        {
            lock (gate)
            {
                return new DesignerSessionSnapshot(
                    ProjectName,
                    isDirty,
                    changeVersion,
                    animationIndex,
                    frameIndex,
                    lastSavedAt,
                    validation,
                    history.Current,
                    autosave.Current,
                    liveDebug.Current,
                    persistenceException);
            }
        }

        private void HandleAutosaveChanged(AutosaveSnapshot snapshot) => Publish();
        private void HandleLiveDebugChanged(LiveDebugSnapshot snapshot) => Publish();
        private void HandleHistoryChanged(CommandHistorySnapshot snapshot) => Publish();

        private void Publish()
        {
            Action<DesignerSessionSnapshot> handler = StateChanged;
            if (handler == null) return;
            DesignerSessionSnapshot snapshot = CreateSnapshot();
            if (eventContext == null) handler(snapshot);
            else eventContext.Post(value => handler((DesignerSessionSnapshot)value), snapshot);
        }

        private static long EstimateAnimationBytes(AnimationState animation)
        {
            long bytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (var frame in animation.Frames) bytes += EstimateFrameBytes(frame);
            return bytes;
        }

        private static long EstimateFrameBytes(Frame frame)
        {
            long bytes = Config.Command.EstimatedCommandOverheadBytes;
            foreach (var layer in frame.Layers) bytes += EstimateLayerBytes(layer);
            return bytes;
        }

        private static long EstimateLayerBytes(Layer layer)
        {
            return Config.Command.EstimatedCommandOverheadBytes +
                ((long)layer.Pixels.Length * sizeof(uint));
        }

        private AnimationState GetSelectedAnimation()
        {
            ValidateAnimationIndex(animationIndex);
            return Project.Animations[animationIndex];
        }

        private void ValidateAnimationIndex(int index)
        {
            if (index < Config.Common.FirstIndex || index >= Project.Animations.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateFrameIndex(AnimationState animation, int index)
        {
            if (index < Config.Common.FirstIndex || index >= animation.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void ValidateLayerIndex(Frame frame, int index)
        {
            if (index < Config.Common.FirstIndex || index >= frame.Layers.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static void Move<T>(System.Collections.Generic.List<T> values, int fromIndex, int toIndex)
        {
            T value = values[fromIndex];
            values.RemoveAt(fromIndex);
            values.Insert(toIndex, value);
        }

        private static void NormalizeFrameIndices(AnimationState animation)
        {
            for (int index = Config.Common.FirstIndex; index < animation.Frames.Count; index++)
                animation.Frames[index].FrameIndex = index;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DesignerSession));
        }
    }
}
