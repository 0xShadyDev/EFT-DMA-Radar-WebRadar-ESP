using SharpDX.Direct2D1;
using SharpDX;
using System.Drawing;
using SharpDX.Mathematics.Interop;

namespace eft_dma_radar
{
    public class BrushManager : IDisposable
    {
        private readonly WindowRenderTarget _device;
        private readonly Dictionary<string, SolidColorBrush> _brushes;
        private bool _disposed;

        public BrushManager(WindowRenderTarget device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _brushes = new Dictionary<string, SolidColorBrush>();
            InitializeBrushes();
        }

        private void InitializeBrushes()
        {
            // Standard colors
            CreateBrush("WHITE", new RawColor4(1.0f, 1.0f, 1.0f, 10.0f));
            CreateBrush("BLACK", new RawColor4(0.0f, 0.0f, 0.0f, 10.0f));
            CreateBrush("RED", new RawColor4(1.0f, 0.0f, 0.0f, 10.0f));
            CreateBrush("GREEN", new RawColor4(0.0f, 1.0f, 0.0f, 10.0f));
            CreateBrush("BLUE", new RawColor4(0.0f, 0.0f, 1.0f, 10.0f));
            CreateBrush("YELLOW", new RawColor4(1.0f, 1.0f, 0.0f, 10.0f));
            CreateBrush("CYAN", new RawColor4(0.0f, 1.0f, 1.0f, 10.0f));
            CreateBrush("MAGENTA", new RawColor4(1.0f, 0.0f, 1.0f, 10.0f));
            CreateBrush("ORANGE", new RawColor4(1.0f, 0.65f, 0.0f, 10.0f));
            CreateBrush("PURPLE", new RawColor4(0.5f, 0.0f, 0.5f, 10.0f));
            CreateBrush("GRAY", new RawColor4(0.5f, 0.5f, 0.5f, 10.0f));
            CreateBrush("LIGHT_GRAY", new RawColor4(0.83f, 0.83f, 0.83f, 10.0f));
            CreateBrush("TRANSPARENCY", new RawColor4(0.0f, 0.0f, 0.0f, 10.0f));

            // Game-specific colors
            CreateBrush("QUEST", new RawColor4(0.96f, 0.15f, 0.77f, 10.0f));
            CreateBrush("LOOSE_LOOT", new RawColor4(0.15f, 0.73f, 0.96f, 10.0f));
            CreateBrush("CONTAINER_LOOT", new RawColor4(0.24f, 0.93f, 0.71f, 10.0f));
            CreateBrush("CORPSE", new RawColor4(0.90f, 0.20f, 0.66f, 10.0f));

            // Additional game colors
            CreateBrush("TEAMMATE", new RawColor4(0.0f, 1.0f, 0.0f, 10.0f));
            CreateBrush("ENEMY", new RawColor4(1.0f, 0.0f, 0.0f, 10.0f));
            CreateBrush("SCAV", new RawColor4(1.0f, 1.0f, 0.0f, 10.0f));
        }

        private void CreateBrush(string name, RawColor4 color)
        {
            _brushes[name] = new SolidColorBrush(_device, color);
        }

        public SolidColorBrush GetBrush(string name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BrushManager));

            return _brushes.TryGetValue(name, out var brush) ? brush : _brushes["WHITE"];
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var brush in _brushes.Values)
                {
                    brush?.Dispose();
                }
                _brushes.Clear();
                _disposed = true;
            }
        }
    }
}
