#!/usr/bin/env python
"""Export intfloat/multilingual-e5-small to out/knowledge/model/ for the C# query embedder:
model.onnx (inputs input_ids, attention_mask -> last_hidden_state), sentencepiece.bpe.model, tokenizer_ref.json
(reference token ids + embeddings for a few queries, used to verify the C# tokenizer)."""
import json, shutil
from pathlib import Path
import numpy as np, torch
from transformers import AutoModel, AutoTokenizer
from huggingface_hub import hf_hub_download

OUT = Path(__file__).resolve().parents[2] / "out" / "knowledge" / "model"
MODEL = "intfloat/multilingual-e5-small"
SAMPLES = ["query: H형강 400 단면", "query: 고장력 볼트 M20", "query: new project", "query: 도면 치수 편집 방법"]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    tok = AutoTokenizer.from_pretrained(MODEL)
    m = AutoModel.from_pretrained(MODEL).eval()

    class W(torch.nn.Module):
        def __init__(self, m):
            super().__init__(); self.m = m
        def forward(self, input_ids, attention_mask):
            return self.m(input_ids=input_ids, attention_mask=attention_mask).last_hidden_state

    enc = tok(["query: sample"], return_tensors="pt")
    torch.onnx.export(W(m), (enc["input_ids"], enc["attention_mask"]), str(OUT / "model.onnx"),
                      input_names=["input_ids", "attention_mask"], output_names=["last_hidden_state"],
                      dynamic_axes={"input_ids": {0: "b", 1: "s"}, "attention_mask": {0: "b", 1: "s"},
                                    "last_hidden_state": {0: "b", 1: "s"}},
                      opset_version=17, dynamo=False)
    shutil.copy(hf_hub_download(MODEL, "sentencepiece.bpe.model"), OUT / "sentencepiece.bpe.model")
    ref = []
    for s in SAMPLES:
        e = tok([s], return_tensors="pt")
        with torch.no_grad():
            h = m(**e).last_hidden_state
        mask = e["attention_mask"].unsqueeze(-1).float()
        v = (h * mask).sum(1) / mask.sum(1)
        v = torch.nn.functional.normalize(v, dim=-1)[0].numpy()
        ref.append({"text": s, "ids": e["input_ids"][0].tolist(), "vec": [round(float(x), 6) for x in v]})
    (OUT / "tokenizer_ref.json").write_text(json.dumps(ref, ensure_ascii=False), encoding="utf-8")
    print("exported", OUT)


if __name__ == "__main__":
    main()
