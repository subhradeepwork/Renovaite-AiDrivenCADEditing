# index_qdrant.py
import json
import os
from typing import Dict, Any, List
from dotenv import load_dotenv
from qdrant_client import QdrantClient
from qdrant_client.http.exceptions import UnexpectedResponse
from qdrant_client.models import VectorParams, Distance, PointStruct
from embeddings import get_embedder

# Optional progress bar
try:
    from tqdm import tqdm
except Exception:
    tqdm = None

load_dotenv()  # read .env

COLLECTION   = os.getenv("COLLECTION", "architectural_objects")
QDRANT_HOST  = os.getenv("QDRANT_HOST", "localhost")
QDRANT_PORT  = int(os.getenv("QDRANT_PORT", "6333"))
FORCE_RECREATE = os.getenv("FORCE_RECREATE", "0") == "1"
BATCH_SIZE   = int(os.getenv("EMBED_BATCH", "256"))  # Adjust if you hit rate limits
OBJECTS_PATH = os.getenv("OBJECT_SUMMARIES_PATH", "exports/object_summaries.json")

def build_text(obj: Dict[str, Any]) -> str:
    parts = [
        obj.get("name", ""),
        obj.get("ifc_type_final", ""),
        obj.get("meshSummaryText", ""),
        obj.get("ifcSummaryTextShort", ""),
        " ".join(obj.get("tags", []) or []),
    ]
    return " | ".join([p for p in parts if p])

def ensure_collection(client: QdrantClient, dim: int):
    exists = client.collection_exists(COLLECTION)
    if not exists or FORCE_RECREATE:
        if exists and FORCE_RECREATE:
            client.delete_collection(COLLECTION)
            print(f"[index] Deleted existing collection '{COLLECTION}' (FORCE_RECREATE=1).")
        client.create_collection(
            collection_name=COLLECTION,
            vectors_config=VectorParams(size=dim, distance=Distance.COSINE),
        )
        print(f"[index] Created collection '{COLLECTION}' (dim={dim}).")
    else:
        print(f"[index] Using existing collection '{COLLECTION}' (ensure dim matches).")

def chunked(seq: List[Any], n: int):
    for i in range(0, len(seq), n):
        yield i, seq[i:i+n]

def embed_batch(texts: List[str], embed_one, openai_batch=False):
    """
    If OpenAI key is present, we’ll try to batch via the SDK directly for speed.
    Otherwise, fall back to per-item embed_one.
    """
    if not openai_batch:
        return [embed_one(t) for t in texts]

    # Batch via OpenAI (same model as embeddings.get_embedder uses)
    from openai import OpenAI
    import os
    client = OpenAI(api_key=os.getenv("OPENAI_API_KEY"))
    model = os.getenv("EMBED_MODEL", "text-embedding-3-small")
    # guard empty strings
    safe_texts = [t if (t and t.strip()) else " " for t in texts]
    resp = client.embeddings.create(model=model, input=safe_texts)
    return [d.embedding for d in resp.data]

def main(path=OBJECTS_PATH, host=None, port=None):
    host = host or QDRANT_HOST
    port = port or QDRANT_PORT

    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    objects: List[Dict[str, Any]] = data["objects"]

    # Decide embedding mode
    embed_one = get_embedder()               # str -> vector (OpenAI or fake)
    dim = len(embed_one("probe"))

    # If OPENAI_API_KEY is set, we can do true batch requests
    OPENAI = os.getenv("OPENAI_API_KEY") is not None
    if OPENAI:
        print(f"[index] OpenAI embeddings enabled. Batch size = {BATCH_SIZE}.")
    else:
        print("[index] No OPENAI_API_KEY found; using fake embeddings (fast but not semantic).")

    client = QdrantClient(host=host, port=port)
    ensure_collection(client, dim)

    total = len(objects)
    iterator = chunked(objects, BATCH_SIZE)
    if tqdm:
        iterator = tqdm(iterator, total=(total + BATCH_SIZE - 1)//BATCH_SIZE, desc="Indexing")

    point_id = 0
    batch_count = 0
    for start_idx, batch_objs in iterator:
        texts = [build_text(o) for o in batch_objs]
        try:
            vectors = embed_batch(texts, embed_one, openai_batch=OPENAI)
        except Exception as e:
            # If batch call fails for any reason, fall back to per-item
            print(f"[index] Batch embed failed ({e}); falling back to per-item for this chunk.")
            vectors = [embed_one(t) for t in texts]

        # Build points and upsert per-batch
        points = []
        for i, (obj, vec) in enumerate(zip(batch_objs, vectors)):
            uid = obj["unique_id"]
            payload = {
                "unique_id": uid,
                "scene_id": data.get("scene_id"),
                "name": obj.get("name"),
                "ifc_type_final": obj.get("ifc_type_final"),
                "tags": obj.get("tags", []),
                "numeric": obj.get("numeric", {}),
                "has_geometry": obj.get("has_geometry", True),
                "meshSummaryText": obj.get("meshSummaryText", ""),
                "ifcSummaryTextShort": obj.get("ifcSummaryTextShort", "")
            }
            points.append(PointStruct(id=point_id + i, vector=vec, payload=payload))

        try:
            client.upsert(collection_name=COLLECTION, points=points)
        except UnexpectedResponse as e:
            raise RuntimeError(
                "Upsert failed (likely vector size mismatch). "
                "Set FORCE_RECREATE=1 in .env and re-run indexing.\n"
                f"Original error: {e}"
            )

        point_id += len(points)
        batch_count += 1

    print(f"[index] Indexed {total} objects into '{COLLECTION}' at {host}:{port} in {batch_count} batches.")

if __name__ == "__main__":
    main()
