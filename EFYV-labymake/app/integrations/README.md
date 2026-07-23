# LabyMake Integrations

[Back to application source](../README.md)

`labymake_engine.efyv` binds the app to the descriptor-checked `labymake-engine` service. The
declaration names its protobuf contract and bounded operations; executable export/validation code
lives in the [service implementation](../../services/labymake-engine/README.md).

The declaration intentionally binds the unary v1 calls today. The v2 transfer service's `GetLimits`
and streaming `Export` are additive capabilities ready for the generic Platform transport; declaring them here
before Platform supports streaming would make the generated app promise a call shape it cannot yet
execute.
