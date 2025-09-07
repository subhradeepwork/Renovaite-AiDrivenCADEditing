# api.py
import os, hashlib
from typing import Optional, List, Dict, Any
from dotenv import load_dotenv
from fastapi import FastAPI
from pydantic import BaseModel
from qdrant_client import QdrantClient
from qdrant_client.models import (
    Filter, FieldCondition, MatchValue, MatchAny, Range, PointStruct
)
from embeddings import get_embedder

load_dotenv()  # read .env

COLLECTION = os.getenv("COLLECTION", "architectural_objects")
QDRANT_HOST = os.getenv("QDRANT_HOST", "localhost")
QDRANT_PORT = int(os.getenv("QDRANT_PORT", "6333"))

# ---------- Embedding ----------
embed = get_embedder()

def build_text_from_payload(pl: Dict[str, Any]) -> str:
    """Rebuild the same text used at indexing for /similar and /upsert."""
    parts = [
        pl.get("name", ""),
        pl.get("ifc_type_final", ""),
        pl.get("ifcSummaryTextShort", "") or "",
        pl.get("meshSummaryText", "") or "",
        " ".join(pl.get("tags", []) or []),
    ]
    return " | ".join([p for p in parts if p])

app = FastAPI()
client = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT)

# ---------- Schemas ----------
class SearchReq(BaseModel):
    query: str
    top_k: int = 5

    # semantics
    ifc_type_final: Optional[str] = None
    ifc_types_any: Optional[List[str]] = None

    # tags
    tags_all: Optional[List[str]] = None
    tags_any: Optional[List[str]] = None

    # geometry / complexity
    require_geometry: bool = True
    min_holes: Optional[int] = None
    min_width_m: Optional[float] = None
    max_width_m: Optional[float] = None
    min_height_m: Optional[float] = None
    max_height_m: Optional[float] = None
    min_depth_m: Optional[float] = None
    max_depth_m: Optional[float] = None
    min_triangles: Optional[int] = None
    max_triangles: Optional[int] = None
    min_vertices: Optional[int] = None
    max_vertices: Optional[int] = None

    # sorting
    sort_by: Optional[str] = "score"   # "score"|"bbox_width"|"bbox_height"|"bbox_depth"|"triangle_count"|"vertex_count"
    sort_dir: Optional[str] = "desc"   # "asc"|"desc"

class SimilarReq(BaseModel):
    unique_id: str
    top_k: int = 5
    # optional same filters as SearchReq
    ifc_type_final: Optional[str] = None
    ifc_types_any: Optional[List[str]] = None
    tags_all: Optional[List[str]] = None
    tags_any: Optional[List[str]] = None
    require_geometry: bool = True

class UpsertReq(BaseModel):
    unique_id: str
    name: str
    ifc_type_final: str
    meshSummaryText: str
    ifcSummaryTextShort: str
    tags: Optional[List[str]] = None
    numeric: Optional[Dict[str, Any]] = None
    has_geometry: Optional[bool] = True
    scene_id: Optional[str] = None

# ---------- Helpers ----------
def build_filter(req: SearchReq | SimilarReq) -> Optional[Filter]:
    must_conds = []

    # ifc type filters
    if getattr(req, "ifc_type_final", None):
        must_conds.append(FieldCondition(key="ifc_type_final", match=MatchValue(value=req.ifc_type_final)))
    if getattr(req, "ifc_types_any", None):
        must_conds.append(FieldCondition(key="ifc_type_final", match=MatchAny(any=req.ifc_types_any)))

    # geometry presence
    if getattr(req, "require_geometry", False):
        must_conds.append(FieldCondition(key="has_geometry", match=MatchValue(value=True)))

    # tags (all -> multiple must; any -> MatchAny)
    if getattr(req, "tags_all", None):
        for t in req.tags_all:
            must_conds.append(FieldCondition(key="tags", match=MatchValue(value=t)))
    if getattr(req, "tags_any", None):
        must_conds.append(FieldCondition(key="tags", match=MatchAny(any=req.tags_any)))

    # opening count (>=)
    if isinstance(req, SearchReq) and req.min_holes is not None:
        must_conds.append(FieldCondition(key="numeric.hole_count", range=Range(gte=req.min_holes)))

    # numeric ranges (bbox, triangles, vertices)
    if isinstance(req, SearchReq):
        if req.min_width_m is not None or req.max_width_m is not None:
            must_conds.append(FieldCondition(
                key="numeric.bbox_width_m",
                range=Range(gte=req.min_width_m, lte=req.max_width_m)
            ))
        if req.min_height_m is not None or req.max_height_m is not None:
            must_conds.append(FieldCondition(
                key="numeric.bbox_height_m",
                range=Range(gte=req.min_height_m, lte=req.max_height_m)
            ))
        if req.min_depth_m is not None or req.max_depth_m is not None:
            must_conds.append(FieldCondition(
                key="numeric.bbox_depth_m",
                range=Range(gte=req.min_depth_m, lte=req.max_depth_m)
            ))
        if req.min_triangles is not None or req.max_triangles is not None:
            must_conds.append(FieldCondition(
                key="numeric.triangle_count",
                range=Range(gte=req.min_triangles, lte=req.max_triangles)
            ))
        if req.min_vertices is not None or req.max_vertices is not None:
            must_conds.append(FieldCondition(
                key="numeric.vertex_count",
                range=Range(gte=req.min_vertices, lte=req.max_vertices)
            ))

    return Filter(must=must_conds) if must_conds else None

def rich_hit(p) -> Dict[str, Any]:
    pl = p.payload or {}
    name = pl.get("name", "")
    typ = pl.get("ifc_type_final", "")
    short_ifc = pl.get("ifcSummaryTextShort", "") or ""
    mesh_text = (pl.get("meshSummaryText", "") or "")[:220]
    ctx = f"{name} | {typ} | {short_ifc} | {mesh_text}".strip(" |")
    numeric = pl.get("numeric") or {}
    return {
        "score": p.score,
        "unique_id": pl.get("unique_id"),
        "name": name,
        "ifc_type_final": typ,
        "snippet": ctx,
        "hole_count": numeric.get("hole_count"),
        "bbox": {
            "w": numeric.get("bbox_width_m"),
            "h": numeric.get("bbox_height_m"),
            "d": numeric.get("bbox_depth_m"),
        },
        "triangles": numeric.get("triangle_count"),
        "vertices": numeric.get("vertex_count"),
        "tags": pl.get("tags", [])
    }

def _stable_int_id_from_uid(uid: str) -> int:
    """Create a stable 63-bit int from unique_id (avoids collisions in practice)."""
    h = hashlib.sha1(uid.encode("utf-8")).digest()
    return int.from_bytes(h[-8:], "big") & ((1 << 63) - 1)

def _find_existing_point_id_by_unique_id(uid: str) -> Optional[int]:
    """Return the existing point ID for this unique_id (if any), else None."""
    flt = Filter(must=[FieldCondition(key="unique_id", match=MatchValue(value=uid))])
    scroll = client.scroll(collection_name=COLLECTION, scroll_filter=flt, limit=1)
    pts = scroll[0] if isinstance(scroll, tuple) else scroll
    if not pts:
        return None
    existing_id = pts[0].id
    try:
        return int(existing_id)
    except Exception:
        return _stable_int_id_from_uid(uid)

# ---------- Endpoints ----------
@app.get("/health")
def health():
    info = client.get_collections()
    return {
        "status": "ok",
        "collections": [c.name for c in info.collections],
        "qdrant": f"{QDRANT_HOST}:{QDRANT_PORT}",
        "collection": COLLECTION
    }

@app.post("/search")
def search(req: SearchReq):
    vec = embed(req.query)
    qfilter = build_filter(req)
    res = client.search(
        collection_name=COLLECTION,
        query_vector=vec,
        limit=req.top_k,
        query_filter=qfilter
    )
    hits = [rich_hit(p) for p in res]
    key_map = {
        "bbox_width": lambda h: (h["bbox"]["w"] or 0.0),
        "bbox_height": lambda h: (h["bbox"]["h"] or 0.0),
        "bbox_depth": lambda h: (h["bbox"]["d"] or 0.0),
        "triangle_count": lambda h: (h["triangles"] or 0),
        "vertex_count": lambda h: (h["vertices"] or 0),
        "score": lambda h: h["score"],
    }
    sort_key = key_map.get((req.sort_by or "score"), key_map["score"])
    hits = sorted(hits, key=sort_key, reverse=(req.sort_dir != "asc"))
    return {"results": hits}

@app.post("/similar")
def similar(req: SimilarReq):
    payload_filter = Filter(must=[FieldCondition(key="unique_id", match=MatchValue(value=req.unique_id))])
    scroll = client.scroll(collection_name=COLLECTION, scroll_filter=payload_filter, limit=1)
    points = scroll[0] if isinstance(scroll, tuple) else scroll
    if not points:
        return {"results": []}
    pl = points[0].payload or {}
    vec = embed(build_text_from_payload(pl))
    qfilter = build_filter(req)
    res = client.search(
        collection_name=COLLECTION,
        query_vector=vec,
        limit=req.top_k + 1,
        query_filter=qfilter
    )
    hits = [rich_hit(p) for p in res if (p.payload or {}).get("unique_id") != req.unique_id]
    return {"results": hits[:req.top_k]}

@app.post("/upsert")
def upsert(req: UpsertReq):
    """
    Add or update a single object in Qdrant.
    - If unique_id exists -> update vector + payload
    - Else -> insert a new point with a stable numeric id derived from unique_id
    """
    payload = {
        "unique_id": req.unique_id,
        "scene_id": req.scene_id,
        "name": req.name,
        "ifc_type_final": req.ifc_type_final,
        "tags": req.tags or [],
        "numeric": req.numeric or {},
        "has_geometry": bool(req.has_geometry),
        "meshSummaryText": req.meshSummaryText or "",
        "ifcSummaryTextShort": req.ifcSummaryTextShort or "",
    }
    pl_for_text = {
        "name": req.name,
        "ifc_type_final": req.ifc_type_final,
        "ifcSummaryTextShort": req.ifcSummaryTextShort,
        "meshSummaryText": req.meshSummaryText,
        "tags": req.tags or [],
    }
    vec = embed(build_text_from_payload(pl_for_text))
    point_id = _find_existing_point_id_by_unique_id(req.unique_id)
    if point_id is None:
        point_id = _stable_int_id_from_uid(req.unique_id)
    point = PointStruct(id=point_id, vector=vec, payload=payload)
    client.upsert(collection_name=COLLECTION, points=[point])
    return {"status": "ok", "updated": req.unique_id, "point_id": point_id}
