# EFYV LabyMake

[Labyrinth system](../README.md) / LabyMake node

LabyMake is a regular declaration-driven EFYV node. [`app/app.efyv`](app/app.efyv) selects the
generic `@efyv/maker` studio and the core platform services. The only node-owned executable is the
narrow [`labymake-engine`](services/labymake-engine/) gRPC service for bounded validation and
deterministic Unity artifact export.

The Unity game and `EFYV-labybackend` deliberately live outside this node root. They consume exported
artifacts and are not packaged into the Kubernetes node.

## Browse

- [Application declaration](app/README.md): EFYV pages, maker configuration, and integrations.
- [Services](services/README.md): the node-owned engine and its protocol boundary.

Compile and regenerate through the shared platform tooling:

```powershell
efyv-ci compile-plan --project .
efyv-appgen build-node --node . --plan .efyv/build/platform-plan.json
```

The generated topology contains only the five core services plus `labymake-engine`; optional
observability, backup/restore, logging, and mobile services appear only when declared in EFYV.
