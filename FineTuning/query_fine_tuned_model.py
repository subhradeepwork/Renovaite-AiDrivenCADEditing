from openai import OpenAI

# ✅ Replace with your OpenAI key and fine-tuned model name when training is complete
client = OpenAI(api_key="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")

fine_tuned_model = "ft:gpt-3.5-turbo-0125:personal::BzsRfp2D"  # Replace this with your real model ID

# 👤 Sample prompt
user_prompt = "Increase the width of the door by 2 feet."

response = client.chat.completions.create(
    model=fine_tuned_model,
    messages=[
        {"role": "system", "content": "You are a helpful assistant that returns structured JSON instructions for Unity object modifications."},
        {"role": "user", "content": user_prompt}
    ],
    temperature=0.2
)

print("📤 Prompt:", user_prompt)
print("\n🧠 Model Response:")
print(response.choices[0].message.content)
