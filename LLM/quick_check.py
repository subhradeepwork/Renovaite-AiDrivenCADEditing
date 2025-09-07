# quick_check.py
# Quick environment + dependency sanity check for RenovAite backend.
# - Loads .env (if present)
# - Prints critical env vars (masked)
# - Verifies OpenAI client (v1 preferred; falls back to legacy v0)
# - Gets an embedding and prints its dimension
# - Connects to Qdrant and prints the collection vectors config
# Run:  python quick_check.py

import os
import sys

def mask(v: str, keep: int = 6) -> str:
    if not v:
        return "(missing)"
    v = str(v)
    return (v[:keep] + "..." if len(v) > keep else v)

print("=== RenovAite quick_check ===")
print("PYTHON:", sys.version)

# -------------------------------------------------------------------
# 1) Load .env early (if python-dotenv is installed)
# -------------------------------------------------------------------
loaded_env = False
try:
    from dotenv import load_dotenv, find_dotenv  # type: ignore
    env_path = find_dotenv(usecwd=True)
    load_dotenv(env_path or None)
    loaded_env = True
    print(f".env loaded: {bool(env_path)}  path: {env_path or '(search default)'}")
except Exception as e:
    print(f".env not loaded (python-dotenv missing or error: {e}). "
          f"Continuing with process environment only.")

# -------------------------------------------------------------------
# 2) Critical environment variables
# -------------------------------------------------------------------
CRIT_VARS = ["OPENAI_API_KEY", "QDRANT_URL", "QDRANT_COLLECTION", "EMBEDDING_MODEL"]
vals = {k: os.getenv(k) for k in CRIT_VARS}
print("ENV:")
for k in CRIT_VARS:
    print(f"  {k:18} = {mask(vals.get(k))}")

# Provide sensible default for EMBEDDING_MODEL if not set
EMBEDDING_MODEL = vals.get("EMBEDDING_MODEL") or "text-embedding-3-small"

# -------------------------------------------------------------------
# 3) OpenAI embedding check (v1 preferred; fallback to v0)
# -------------------------------------------------------------------
embed_dim = None
openai_mode = None
embed_error = None
try:
    # Try v1 client first
    from openai import OpenAI  # type: ignore
    openai_mode = "v1"
    client = OpenAI(api_key=os.getenv("OPENAI_API_KEY"))
    vec = client.embeddings.create(model=EMBEDDING_MODEL, input="x").data[0].embedding
    embed_dim = len(vec)
    print(f"OpenAI: v1 client OK  model={EMBEDDING_MODEL}  dim={embed_dim}")
except Exception as e_v1:
    # Try legacy v0
    try:
        import openai  # type: ignore
        openai_mode = "v0"
        openai.api_key = os.getenv("OPENAI_API_KEY")
        resp = openai.Embedding.create(model=EMBEDDING_MODEL, input="x")
        vec = resp["data"][0]["embedding"]
        embed_dim = len(vec)
        print(f"OpenAI: v0 client OK  model={EMBEDDING_MODEL}  dim={embed_dim}")
    except Exception as e_v0:
        embed_error = f"OpenAI check failed. v1_error={e_v1}; v0_error={e_v0}"
        print(embed_error)

# -------------------------------------------------------------------
# 4) Qdrant connectivity + collection schema
# -------------------------------------------------------------------
try:
    from qdrant_client import QdrantClient  # type: ignore
    qc = QdrantClient(url=os.getenv("QDRANT_URL"))
    col = os.getenv("QDRANT_COLLECTION")
    if not col:
        raise RuntimeError("QDRANT_COLLECTION is not set.")
    info = qc.get_collection(col)
    print("Qdrant: connected ✅")
    print("  collection:", col)
    print("  vectors_config:", getattr(info, "vectors_config", info))
except Exception as e:
    print(f"Qdrant check failed: {e}")

# -------------------------------------------------------------------
# 5) Quick guidance based on findings
# -------------------------------------------------------------------
print("\n=== Guidance ===")
if not os.getenv("OPENAI_API_KEY"):
    print("- Set OPENAI_API_KEY (in .env or environment).")
if not os.getenv("QDRANT_URL"):
    print("- Set QDRANT_URL (e.g., http://localhost:6333) and ensure Qdrant is running.")
if not os.getenv("QDRANT_COLLECTION"):
    print("- Set QDRANT_COLLECTION (e.g., renovaite).")
if embed_dim is None:
    print("- OpenAI embedding failed. Verify key, library version (v1 recommended), and network access.")
else:
    print(f"- Embedding dim = {embed_dim}. Make sure your Qdrant collection uses the same vector size.")
print("- If Qdrant connection failed: start it with Docker, e.g.:")
print("    docker run -p 6333:6333 -p 6334:6334 -v qdrant_storage:/qdrant/storage qdrant/qdrant:latest")
print("=== /quick_check ===")
