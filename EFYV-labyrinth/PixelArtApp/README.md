# Mock Pixel Art Exporter

[Up to game repository](../README.md) | [LabyMake](../../EFYV-labymake/README.md)

[`MockExporter.py`](MockExporter.py) produces a solid RGBA PNG and matching `.efyvlaby` document for exercising the Unity import contract without the full designer application.

It validates file stems, rejects invalid PNG dimensions and pixel layouts, writes GUID-named temporary files with exclusive-create semantics (mirroring the backend `FastExporter` convention), and publishes metadata last as the completion signal. It is a contract fixture, not the production LabyMake exporter; `../Tests/test_config_contract.py` guards its constants against the backend config.

```powershell
python MockExporter.py
python -m unittest discover -s ..\Tests -p "test_*.py" -v
```

The script's direct-entry mode targets `Assets/RawArt`; use its function with a temporary directory in automation.
