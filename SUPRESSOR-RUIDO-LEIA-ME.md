# Supressor de ruído — o que dá pra usar (e o que eu implementei)

Você pediu Krisp ou algo por GPU (NVIDIA/AMD), grátis e eficiente. Segue o cenário real e o que já ficou pronto.

## As opções, sem enrolação

- **Krisp** — é um SDK **pago/licenciado**. Não dá pra "implementar Krisp" de graça; exige contrato/licença. Descartado por custo.
- **NVIDIA (RTX Voice / Broadcast / Maxine Audio Effects SDK)** — o SDK é gratuito, mas **só funciona em placa NVIDIA RTX**, é pesado (modelos + DLLs nativas grandes), e é interop C++ complicado. Não atende AMD nem placas antigas.
- **AMD** — a supressão de ruído da AMD vive dentro do **AMD Software (driver)**; **não existe SDK público** para embutir num app. Não dá pra integrar por aplicativo.
- **RNNoise** — supressor por **rede neural, open-source, grátis, leve (roda na CPU)** e **funciona em qualquer placa**. É o que o Discord usou por muito tempo. **É a escolha certa para "grátis e eficiente"** — e foi o que eu integrei.

## O que já está no app

- Integrei o **RNNoise**. Quando ele está disponível, o app usa ele no lugar do supressor simples (bem melhor).
- Se o RNNoise **não** estiver presente, o app cai automaticamente no supressor embutido de antes (nada quebra).
- O liga/desliga é o mesmo de sempre (a opção de "supressão de ruído" nas configurações).

## Para ativar o RNNoise (uma vez)

Falta só colocar a DLL nativa `rnnoise.dll` na pasta:

```
src\NyxarConcord\rnnoise\rnnoise.dll
```

O projeto já está configurado para copiar essa DLL para junto do `.exe` ao compilar (igual ao FFmpeg). Assim que ela estiver lá e você recompilar, o RNNoise entra em ação sozinho.

Onde conseguir a `rnnoise.dll` (x64):
- Projeto oficial: https://github.com/xiph/rnnoise (compilar em 64 bits) — gera a lib do RNNoise.
- Ou use uma build pré-compilada de 64 bits do RNNoise em que você confie.

Importante: precisa ser **64 bits (x64)**, senão o app não carrega a DLL (e volta pro supressor simples).

Se preferir, me manda que eu te ajudo a montar um script para baixar/compilar a `rnnoise.dll` certinha.

## Futuro (se quiser)

Dá pra, mais pra frente, detectar placa NVIDIA RTX e usar o Maxine só nesses PCs, mantendo o RNNoise como padrão para todo o resto. É bem mais trabalho e só vale se muita gente tiver RTX.
