# Mock Pixel Art Exporter

[Up to game repository](../README.md) | [LabyMake](../../EFYV-labymake/README.md)

[`MockExporter.py`](MockExporter.py) produces a solid RGBA PNG and matching `.efyvlaby` document for exercising the Unity import contract without the full designer application.

It validates file stems, writes temporary files, and publishes metadata last as the completion signal. It is a contract fixture, not the production LabyMake exporter.

```powershell
python MockExporter.py
python -m unittest discover -s ..\Tests -p "test_*.py" -v
```

The script's direct-entry mode targets `Assets/RawArt`; use its function with a temporary directory in automation.
