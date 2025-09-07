# embeddings.py
import os
import hashlib
from typing import List
from dotenv import load_dotenv

# Load environment variables from .env (same folder or parent)
load_dotenv()

def _fake_embed(text: str) -> List[float]:
    """Deterministic, cheap fallback (NOT semantic)."""
    h = hashlib.sha256((text or " ").encode("utf-8")).digest()
    return [(b - 128) / 128.0 for b in h[:32]]  # 32-dim fallback

def get_embedder():
    """
    Returns a callable: str -> List[float]
    Prefers OpenAI embeddings if OPENAI_API_KEY is set; otherwise falls back to _fake_embed.
    """
    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        print("[embeddings] No OPENAI_API_KEY found in environment/.env. Using fake embeddings.")
        return _fake_embed

    try:
        from openai import OpenAI
        client = OpenAI(api_key=api_key)
        model = os.getenv("EMBED_MODEL", "text-embedding-3-small")  # 1536-dim

        def _openai_embed(text: str) -> List[float]:
            txt = text if (text and text.strip()) else " "
            resp = client.embeddings.create(model=model, input=txt)
            return resp.data[0].embedding

        # Smoke test
        _ = _openai_embed("probe")
        print(f"[embeddings] Using OpenAI model='{model}'.")
        return _openai_embed
    except Exception as e:
        print(f"[embeddings] ERROR initializing OpenAI embeddings: {e}\nFalling back to fake embeddings.")
        return _fake_embed
