# recreate_collection.py — (re)create your Qdrant collection with the correct vector size.
# Safe to run anytime; it recreates the collection if it exists.

import os
from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams

COL = os.getenv("QDRANT_COLLECTION", "architectural_objects")
URL = os.getenv("QDRANT_URL", "http://localhost:6333")
MODEL = os.getenv("EMBEDDING_MODEL", "text-embedding-3-small")

# Map common OpenAI embedding models to known dims
MODEL_DIMS = {
    "text-embedding-3-small": 1536,
    "text-embedding-3-large": 3072,
    # Add more mappings if you use others
}
DIM = MODEL_DIMS.get(MODEL, 1536)

qc = QdrantClient(url=URL)

qc.recreate_collection(
    collection_name=COL,
    vectors_config=VectorParams(size=DIM, distance=Distance.COSINE)
)
print(f"OK: '{COL}' ready at {URL} (dim={DIM}, distance=COSINE)")
