from openai import OpenAI
import time

# ✅ Replace with your actual API key
client = OpenAI(api_key="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")

# ✅ File name
jsonl_file_path = "unity_llm_training_100.jsonl"

print("📤 Uploading training file...")
with open(jsonl_file_path, "rb") as f:
    training_file = client.files.create(file=f, purpose="fine-tune")

print("✅ File uploaded. ID:", training_file.id)

print("🚀 Launching fine-tuning job...")
job = client.fine_tuning.jobs.create(
    training_file=training_file.id,
    model="gpt-3.5-turbo"
)

print("🎯 Fine-tuning job started! Job ID:", job.id)

print("\n🕒 Polling job status... (press Ctrl+C to stop)")
while True:
    job_status = client.fine_tuning.jobs.retrieve(job.id)
    print(f"⏳ Status: {job_status.status} | Trained tokens: {job_status.trained_tokens}")
    if job_status.status in ["succeeded", "failed", "cancelled"]:
        break
    time.sleep(10)

if job_status.status == "succeeded":
    print("\n✅ Fine-tuning complete!")
    print("📦 Fine-tuned model ID:", job_status.fine_tuned_model)
else:
    print("\n❌ Fine-tuning failed or was cancelled.")
