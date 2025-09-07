# RenovAite — AI-Driven Architectural Editing (IFC + LLM + RAG + Tool-Calling)
The goal: make complex CAD/BIM edits as simple as a conversation, and remove the barrier of dealing with heavy, technical tools that AEC engineers usually face.

RenovAite turns natural language into precise, validated edit operations on architectural models in Unity. It fuses **IFC/BIM metadata**, **RAG search with Qdrant**, and **OpenAI tool-calling** to perform actions like _scale, move, rotate, material, replace, delete_ across single or multiple targets (e.g., “scale all **similar** exterior concrete walls by 10cm”).

![FirstImage](https://github.com/subhradeepwork/Renovaite-AiDrivenCADEditing/blob/2392721528d5c0dc1ed83fb8760ccc23f68fbc04/GIFs/part1.gif)

- **Tech pillars:** Unity (runtime editor), IFC parsing, Qdrant vector search (via FastAPI), OpenAI tool-calling, robust normalization/validation/dispatch, safe preview/apply UX.
- **Outcome:** A production-style pipeline from **prompt → tool call → validation → normalization → target resolution → preview → apply**, with clear extensibility and testability.

RenovAite couples schema-guided tool-calling with a spatially aware RAG pipeline to turn free-form language into deterministic scene edits. 

Instead of traditional document-centric retrieval, we index the 3D world itself: 
- every object is summarized into a dense, IFC-aware record (unique ID, type, aliases/tags, mesh numerics like bounds/triangle counts, and a compact natural-language summary). 

These records are embedded and stored in Qdrant, enabling both semantic search (“concrete walls with windows nearby”) and similarity expansion from a selected seed object (k-NN with top_k and threshold), with optional filters over IFC type/tags/numerics.

![SecondImage](https://github.com/subhradeepwork/Renovaite-AiDrivenCADEditing/blob/2392721528d5c0dc1ed83fb8760ccc23f68fbc04/GIFs/part2.gif)


At inference, the LLM receives:

(1) a strict tool schema that forces a single, valid action call;

(2) compact object JSON for the selected element; and 

(3) a RAG context JSON of nearby/similar objects. 

The model is instruction-tuned/prompts are engineered to prefer deltas for relative phrasing (e.g., position_delta, rotation_delta) and to emit a selection block that precisely scopes edits (selected/ids/filters/similar). 
A normalization stage canonicalizes units (meters, Euler degrees), resolves array→object vectors, derives scale from width/height/depth deltas via world bounds, and prunes unknown fields—yielding a stable, typed AIInstruction. 

Finally, target resolution maps the selection to actual GameObjects (via registry or Qdrant), the preview layer visualizes impact (outline + change list), and appliers execute moves/rotations/scales/material swaps/replacements/deletions with guardrails (e.g., suppress absolute moves on multi-targets). 

The result is an operator-in-the-loop system that blends LLM flexibility with verifiable, IFC-grounded control—portable across scenes, auditable end-to-end, and easy to extend with new actions, retrieval features, or domain heuristics.

![ThirdImage](https://github.com/subhradeepwork/Renovaite-AiDrivenCADEditing/blob/2392721528d5c0dc1ed83fb8760ccc23f68fbc04/GIFs/part3.gif)


---

## Table of Contents

1. [Key Features](#key-features)  
2. [System Architecture](#system-architecture)  
   - [High-Level Diagram](#high-level-diagram)  
   - [Runtime Flow](#runtime-flow)  
3. [Folder Structure](#folder-structure)  
4. [Data Contracts](#data-contracts)  
   - [Instruction JSON](#instruction-json)  
   - [Selection Block](#selection-block)  
5. [Backend Services (Python + Qdrant)](#backend-services-python--qdrant)  
6. [Unity Integration](#unity-integration)  
   - [Core Components](#core-components)  
   - [Execution Appliers](#execution-appliers)  
   - [Preview UX](#preview-ux)  
   - [IFC & Selection Utilities](#ifc--selection-utilities)  
7. [Setup & Run](#setup--run)  
   - [Prerequisites](#prerequisites)  
   - [Backend setup](#backend-setup)  
   - [Unity setup](#unity-setup)  
   - [End-to-End sanity test](#end-to-end-sanity-test)  
8. [Extensibility](#extensibility)  
9. [Troubleshooting](#troubleshooting)  
10. [Roadmap](#roadmap)  
11. [Contributing](#contributing)  
12. [License](#license)

---

## Key Features

- **Natural language → deterministic actions** via OpenAI **tool-calling** (one structured call per prompt).
- **Context-aware edits** using IFC/BIM metadata, mesh summaries, and **RAG** context (Qdrant).
- **Multi-target operations** (e.g., “apply to similar”) through an explicit **selection block**.
- **Safety by design:** validation, normalization, and **preview/confirm** before applying.
- **Unity-native:** runs at **runtime**—no editor-only restrictions; works with imported IFC scenes.
- **Modular execution:** add new **appliers** (e.g., “extrude,” “boolean cut”) without touching core.
- **Optional warm-indexing:**  runtime pre-warm with progress UI and cancel-on-stop for smooth similarity/search.


---

## System Architecture

### High-Level Diagram

```
User Prompt
    │
    ▼
AIClient ──► PromptBuilder + ToolSchemaProvider ──► OpenAI (tool-calling)
    │                                               │
    │                         (tool args JSON) ◄────┘
    │
    ├─► Heuristics (SelectionHeuristic, Relative Move/Rotate)
    ├─► AIInstructionNormalizer (units, *_delta, arrays→objects)
    ├─► InstructionDispatcher
    │      ├─► IInstructionValidator (schema/semantic checks)
    │      ├─► IInstructionNormalizer (final canonicalization)
    │      └─► IInstructionExecutor
    │             └─► *Appliers (Scale/Move/Rotate/Material/Replace/Delete)
    │
    └─► TargetResolver (selected / ids / filters / similar via Qdrant)
           └─► QdrantApiClient (FastAPI) ◄─ RagContextBuilder / RagCompactor
```

### Runtime Flow

1. **Selection**: User clicks an object (with IFC metadata).  
2. **Prompting**: User enters a natural prompt (“scale all similar walls by 0.2m”).  
3. **RAG (optional)**: We fetch similar/search hits from Qdrant to give the LLM context.  
4. **Tool-calling**: PromptBuilder + ToolSchemaProvider enforce a strict tool schema.  
5. **Normalization**: Units, delta inference, vector canonicalization, pruning.  
6. **Target resolution**: Selection block maps to actual GameObjects (selected, filter, **similar** via Qdrant).  
7. **Preview**: Highlight all affected targets, show human-readable change list; user accepts/rejects.  
8. **Apply**: InstructionExecutor calls the correct Applier(s).  
9. **Indexing**: QdrantSelectionListener keeps target objects up-to-date in the vector DB on selection.

---

## Folder Structure

> This structure mirrors how the code is organized for readability and low coupling.

```
Assets/_Scripts/
├─ AI/
│  ├─ Core/                         # orchestration
│  │  ├─ AIClient.cs
│  │  ├─ InstructionDispatcher.cs
│  │  └─ TargetResolver.cs
│  ├─ Instructions/
│  │  ├─ Interfaces/                # contracts
│  │  │  ├─ IInstructionValidator.cs
│  │  │  └─ IInstructionNormalizer.cs
│  │  ├─ Models/                    # DTOs
│  │  │  ├─ AIInstructionSchema.cs
│  │  │  └─ AIInstruction.cs
│  │  ├─ Validation/                # checks
│  │  │  └─ AIInstructionValidator.cs
│  │  ├─ Normalization/             # canonicalization
│  │  │  └─ AIInstructionNormalizer.cs
│  │  └─ Heuristics/
│  │     └─ SelectionHeuristic.cs
│  ├─ OpenAI/                       # LLM plumbing
│  │  ├─ PromptBuilder.cs
│  │  ├─ ToolSchemaProvider.cs
│  │  ├─ ResponseParser.cs
│  │  └─ IOpenAIClient.cs
│  ├─ Prompting/
│  │  ├─ ObjectJsonCompactor.cs
│  │  └─ RagCompactor.cs
│  ├─ RAG/
│  │  └─ RagContextBuilder.cs
│  ├─ Qdrant/
│  │  ├─ QdrantApiClient.cs
│  │  ├─ QdrantUpsertBuilder.cs
│  │  ├─ QdrantSelectionListener.cs
│  │  └─ QdrantSettings.cs
│  └─ Execution/                    # effectors
│     ├─ Core/
│     │  ├─ ActionApplierBase.cs
│     │  ├─ InstructionExecutor.cs
│     │  └─ IInstructionExecutor.cs
│     ├─ Transform/
│     │  ├─ MoveApplier.cs
│     │  ├─ RotateApplier.cs
│     │  └─ ScaleApplier.cs
│     ├─ Materials/
│     │  ├─ MaterialApplier.cs
│     │  ├─ MaterialLibrary.cs
│     │  └─ MaterialResolver.cs
│     ├─ Replace/
│     │  └─ ReplaceApplier.cs
│     └─ Delete/
│        └─ DeleteApplier.cs
│
├─ Preview/
│  ├─ ChangeSet.cs
│  ├─ PendingChangesController.cs
│  └─ OutlineHighlighter.cs
│
├─ IFC/
│  ├─ IFCInitializer.cs
│  ├─ IfcPropertyExtractor.cs
│  └─ UniqueIdExtractor.cs
│
├─ SceneIO/
│  ├─ ObjectStructureSerializer.cs
│  ├─ ObjectSummaryExporter.cs
│  └─ ChatDTOs.cs
│
├─ Selection/
│  ├─ SelectionManager.cs
│  └─ SelectableObject.cs
│
├─ Geometry/
│  ├─ MeshSummarizer.cs
│  └─ MeshBuilder.cs
│
├─ UI/
│  └─ PendingChangesPanel.cs
│
├─ Infrastructure/
│  └─ ObjectRegistry.cs
│
└─ UnityOnly/
   ├─ CameraOrbitController.cs
   ├─ ChatManager.cs
   ├─ InstructionInputUI.cs
   └─ AutoSetupIfcModel.cs
```

> Backend (Python) lives alongside the Unity project (outside `Assets/`), for example:
```
backend/
├─ api.py              # FastAPI (Qdrant proxy)
├─ embeddings.py       # OpenAI embeddings + fallback
└─ index_qdrant.py     # batch indexer for exported object summaries
```

---

## Data Contracts

### Instruction JSON

All model outputs must be **one tool call** with this shape (simplified):

```json
{
  "action": "scale|move|rotate|material|replace|delete",
  "target": { "unique_id": "WALL_00123", "name": "Wall A (optional)" },
  "modification": {
    "scale": { "x": 1.0, "y": 1.1, "z": 1.0 },
    "position": { "x": 0, "y": 0, "z": 0 },
    "position_delta": { "x": 0.5, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 90, "z": 0 },
    "rotation_delta": { "x": 0, "y": 15, "z": 0 },
    "width": 0.2, "height": 0.0, "depth": -0.1,
    "material": { "materialName": "Concrete_Polished", "metallic": 0.2, "smoothness": 0.7 },
    "replace": { "prefabPath": "Assets/Prefabs/NewDoor.prefab" }
  },
  "selection": {
    "apply_to": "selected|ids|filters|similar",
    "ids": ["WALL_00123", "WALL_00456"],
    "top_k": 5,
    "similarity_threshold": 0.85,
    "filters": { "ifc_type": "Wall", "tags": ["external", "concrete"] },
    "exclude_selected": false
  }
}
```

**Rules enforced by PromptBuilder + Normalizer:**
- Use **meters** for distances and **Euler degrees** for rotations.
- Prefer `*_delta` for relative phrasing (“move left by 1m”).
- Keep `target.unique_id` for the seed object even when applying to **similar**.
- Unknown/extra fields are pruned.

### Selection Block

Determines **which** objects are affected:

- `apply_to: "selected"` — only the seed object.  
- `apply_to: "ids"` — explicit list of unique IDs.  
- `apply_to: "filters"` — IFC type/tags filters (local/Qdrant).  
- `apply_to: "similar"` — Qdrant kNN from selected object with `top_k`/threshold.

---

## Backend Services (Python + Qdrant)

The backend provides RAG services and upsert/search/similar endpoints:

- **`api.py` (FastAPI)**  
  - `/upsert` — add/update one object (payload includes IFC, mesh summary, tags, numerics).  
  - `/search` — semantic search with optional filters (ifc_type, tags, numeric ranges).  
  - `/similar` — kNN from a reference object’s text snapshot.  
  - `/health` — service + collection info.

- **`embeddings.py`**  
  - OpenAI embeddings (`text-embedding-3-small`) if `OPENAI_API_KEY` is present; else a deterministic **fake embedding** for dev.

- **`index_qdrant.py`**  
  - Batch indexer for Unity-exported scene summaries (see `ObjectSummaryExporter`).

> Qdrant runs locally or remote; the FastAPI service talks to it via `qdrant-client`.

**.env (backend) example:**
```
OPENAI_API_KEY=sk-...
EMBED_MODEL=text-embedding-3-small
QDRANT_HOST=localhost
QDRANT_PORT=6333
COLLECTION=architectural_objects
```

---

## Unity Integration

### Core Components
- **AIClient** — orchestrates prompt building, RAG context, tool-calling, normalization, preview staging.  
- **InstructionDispatcher** — pure pipeline: validate → normalize → execute.  
- **TargetResolver** — turns the selection block into concrete GameObjects (local/filters/similar via Qdrant).

### Execution Appliers
- **Transform:** `MoveApplier`, `RotateApplier`, `ScaleApplier` (supports *_delta and absolute).  
- **Materials:** `MaterialApplier` (with `MaterialLibrary` + `MaterialResolver`).  
- **Replace:** `ReplaceApplier` (prefab swap with metadata carry-over).  
- **Delete:** `DeleteApplier`.

Attach **InstructionExecutor** to a scene object and assign the Appliers in the Inspector.

### Preview UX
- **PendingChangesController** — highlights affected objects (via `OutlineHighlighter`), shows **PendingChangesPanel** with a list of Name/ID/Action, and **Apply/Reject** controls.  
- **ChangeSet** — bundle of normalized JSON + resolved target list for the preview step.

### IFC & Selection Utilities
- **IFCInitializer** — registers every IFC object into `ObjectRegistry` by Unique ID at runtime.  
- **IfcPropertyExtractor / UniqueIdExtractor** — pull IFC fields and the unique ID.  
- **SelectionManager / SelectableObject** — click to select, build **selected context JSON**, raise `SelectionChanged` events, auto-upsert to Qdrant via `QdrantSelectionListener`.

---

## Setup & Run

### Prerequisites
- **Unity**: a recent LTS (e.g., 6000.0).  
- **Python 3.10+** with `pip`.  
- **Qdrant** (local Docker or managed) and **OpenAI API key** (optional; fake embeddings work for dev).

### Backend setup

1. **Create venv & install deps**
   ```bash
   cd backend
   python -m venv .venv && source .venv/bin/activate    # Windows: .venv\Scripts\activate
   pip install -r requirements.txt

   ```
2. **Configure `.env`** (see example above).
3. **Run FastAPI**
   ```bash
   uvicorn api:app --host 0.0.0.0 --port 8000 --reload
   ```
4. **(Optional) Run Qdrant via Docker**
   ```bash
   docker run -p 6333:6333 -v qdrant_storage:/qdrant/storage qdrant/qdrant
   ```
5. **Index scene objects** (after a Unity export; see below)
   ```bash
   python index_qdrant.py   # expects exported JSON (see script header or env vars)
   ```

### Unity setup

1. **Open the project** in Unity.  
2. **Scene wiring**:
   - Add **ObjectRegistry** (singleton) to the scene.
   - Add **InstructionExecutor** and assign Appliers (`ScaleApplier`, `MoveApplier`, `RotateApplier`, `MaterialApplier`, `ReplaceApplier`, `DeleteApplier`).
   - Add **SelectionManager**; set highlight/wireframe/bounding line materials.
   - Add **PendingChangesController** and **PendingChangesPanel** prefab reference.
   - Add **AIClient**; reference `IOpenAIClient` implementation (your HTTP wrapper), and (optionally) `QdrantApiClient` settings.
3. **IFC objects**: ensure imported objects have `ifcProperties`; run **AutoSetupIfcModel** or add `SelectableObject` + `IFCInitializer`.  
4. **MaterialLibrary**: create `MaterialLibrary` asset and map keys/aliases to materials.

### End-to-End sanity test

- Press **Play** in Unity.  
- Click an object to select it.  
- In the chat box (InstructionInputUI), try:  
  > “scale all similar concrete walls by +0.2m width”  
- A preview panel should show; accept to apply.  
- Verify console logs from Appliers and that highlights clear.

---

## Extensibility

- **Add a new action (“extrude”)**  
  - Create `ExtrudeApplier : ActionApplierBase` and implement `Apply`.  
  - Add a case in `InstructionExecutor`.  
  - Extend `ToolSchemaProvider` schema to allow `modification.extrude` fields.  
  - Extend `AIInstructionNormalizer` (units, arrays→objects, range checks).  
  - Add preview text in `PendingChangesController.DescribeEffectFromJson`.

- **New material resolution rules**  
  - Add aliases/keys in `MaterialLibrary`.  
  - Enhance `MaterialResolver` scoring strategy.

- **Custom selection semantics**  
  - Extend `TargetResolver` to support new filters (e.g., floor/storey).  
  - Mirror in `QdrantApiClient` and FastAPI filters.

---

## Troubleshooting

- **No highlights / preview not appearing**  
  - Ensure `PendingChangesController` is in the scene and panel prefab is assigned.  
  - Check the console for validation errors (invalid instruction).

- **“Target GameObject not found”**  
  - Ensure **IFCInitializer** registered the object in `ObjectRegistry`.  
  - Confirm the `unique_id` exists in `ifcProperties` (case-insensitive “Unique ID”).  
  - For name-based fallback, object must be in the active scene.

- **Absolute move on multiple targets is suppressed**  
  - By design. Use `position_delta` for multi-object moves; the normalizer and executor enforce this.

- **Qdrant returns no results**  
  - Confirm backend `.env` and that Qdrant is reachable.  
  - Re-index with `index_qdrant.py`; check `/health`.  
  - Verify `QdrantSelectionListener` upserts on selection (watch logs).

- **Model returns plain text (no tool call)**  
  - Ensure `ToolSchemaProvider.GetToolChoiceJson()` forces the tool.  
  - Confirm `PromptBuilder.BuildStrictSystemMessage()` is used.

- **Material not applied**  
  - Check `MaterialLibrary` key/alias and that `applyToAllSubmeshes` (if used) is correct.  
  - Clear `MaterialPropertyBlock` if needed (supported in `MaterialApplier`).

---

## Roadmap

- New appliers: **Extrude**, **BooleanCut**, **Parametric openings**.  
- Expanded RAG: scene graph relations (adjacency, containment), more geometric filters.  
- Multi-agent planning for composite edits with **FeasibilityEngine** checks.  
- Assembly Definitions + unit tests per module.  
- Editor tooling: batch validators, repair utilities, scene graph visualizer.

---

## Contributing

1. Fork and branch from `main`.  
2. Use folder-mirrored **namespaces** (e.g., `RenovAite.AI.Execution.Transform`).  
3. Add tests or scene repros for new features if possible.  
4. Open a PR with a clear summary, screenshots/GIFs for UX.

---

## License

_Add your preferred license here (e.g., MIT, Apache-2.0)._

---

### Appendix — Example Tool Call

```json
{
  "action": "rotate",
  "target": { "unique_id": "COLUMN_007" },
  "modification": { "rotation_delta": { "x": 0, "y": 15, "z": 0 } },
  "selection": { "apply_to": "selected" }
}
```
