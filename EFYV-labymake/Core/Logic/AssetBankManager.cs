using System;
using System.Collections.Generic;
using System.IO;
using EFYVLabyMake.Core.Export;
using EFYVLabyMake.Core.IO;
using EFYVLabyMake.Core.Models;
using Config = EFYVBackend.Core.Data.EFYVLabyrinthConfig.LabyMake;

namespace EFYVLabyMake.Core.Logic
{
    public sealed class AssetBankManager
    {
        private readonly string bankDirectory;

        public event Action<string, Exception> LoadFailed;

        public AssetBankManager(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException(nameof(directoryPath));
            bankDirectory = Path.GetFullPath(directoryPath);
            Directory.CreateDirectory(bankDirectory);
        }

        public void SaveSubElement(SubElement element)
        {
            ValidateSubElement(element);
            string path = DesignerPathPolicy.GetContainedPath(
                bankDirectory,
                element.Name + Config.Export.ExtensionEfyvSub);
            string temporaryPath = DesignerPathPolicy.GetContainedPath(bankDirectory, Path.GetRandomFileName());

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Config.Persistence.SubElementFormatVersion);
                    writer.Write(element.Name);
                    writer.Write(element.Width);
                    writer.Write(element.Height);
                    writer.Write(element.Pixels.Length);
                    foreach (uint pixel in element.Pixels) writer.Write(pixel);
                    writer.Flush();
                    stream.Flush(true);
                }

                ExportEngine.AtomicReplace(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public List<SubElement> LoadAllSubElements()
        {
            var elements = new List<SubElement>();
            string[] files = Directory.GetFiles(bankDirectory, Config.Export.WildcardEfyvSub);
            Array.Sort(files, StringComparer.Ordinal);

            foreach (string file in files)
            {
                try
                {
                    elements.Add(LoadSubElement(file));
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is InvalidDataException ||
                    exception is ArgumentException ||
                    exception is OverflowException)
                {
                    LoadFailed?.Invoke(file, exception);
                }
            }

            return elements;
        }

        public unsafe SubElement ExtractFromCanvas(
            Frame frame,
            int startX,
            int startY,
            int width,
            int height,
            string name)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (!DesignerPathPolicy.IsSafeFileStem(name)) throw new ArgumentException(nameof(name));
            ValidateDimensions(width, height);
            if (startX < Config.Canvas.MinCoordinate || startY < Config.Canvas.MinCoordinate ||
                startX > frame.Width - width || startY > frame.Height - height)
                throw new ArgumentOutOfRangeException(nameof(startX));

            PixelColor[] flatCanvas = frame.FlattenLayers();
            uint[] subPixels = new uint[checked(width * height)];

            fixed (PixelColor* source = flatCanvas)
            fixed (uint* destination = subPixels)
            {
                EFYVBackend.Core.Memory.FastMemory.ScaleBlitNearestNeighbor(
                    (uint*)source,
                    frame.Width,
                    frame.Height,
                    destination,
                    width,
                    height,
                    Config.Export.SubElementScale,
                    -startX,
                    -startY);
            }

            return new SubElement(name, width, height, subPixels);
        }

        private static SubElement LoadSubElement(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Config.Persistence.SubElementFormatVersion)
                    throw new InvalidDataException();

                string name = reader.ReadString();
                if (!DesignerPathPolicy.IsSafeFileStem(name)) throw new InvalidDataException();
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int pixelCount = reader.ReadInt32();
                ValidateDimensions(width, height);
                if (pixelCount != checked(width * height) ||
                    pixelCount > Config.Persistence.MaxSubElementPixelCount ||
                    stream.Length - stream.Position != checked((long)pixelCount * sizeof(uint)))
                    throw new InvalidDataException();

                var pixels = new uint[pixelCount];
                for (int index = Config.Common.FirstIndex; index < pixelCount; index++)
                    pixels[index] = reader.ReadUInt32();
                return new SubElement(name, width, height, pixels);
            }
        }

        private static void ValidateSubElement(SubElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (!DesignerPathPolicy.IsSafeFileStem(element.Name)) throw new ArgumentException(nameof(element));
            ValidateDimensions(element.Width, element.Height);
            if (element.Pixels == null ||
                element.Pixels.Length != checked(element.Width * element.Height))
                throw new ArgumentException(nameof(element));
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= Config.Canvas.MinCoordinate || height <= Config.Canvas.MinCoordinate ||
                width > Config.Persistence.MaxSubElementDimension ||
                height > Config.Persistence.MaxSubElementDimension ||
                (long)width * height > Config.Persistence.MaxSubElementPixelCount)
                throw new ArgumentOutOfRangeException(nameof(width));
        }
    }
}
