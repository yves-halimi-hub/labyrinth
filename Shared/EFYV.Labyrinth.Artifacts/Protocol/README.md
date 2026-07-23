# Artifact Transfer Protocol

[Back to the artifact package](../README.md)

`labymake_transfer.proto` defines the versioned, bidirectional streaming seam for bounded snapshots
and content-addressed bundles. It lives outside the current Platform custom-service descriptor
because that generic binder supports unary requests only. The engine links and serves this contract
directly; Platform can adopt the same file once its generic transport supports client streaming.
