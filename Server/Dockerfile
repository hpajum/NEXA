FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

WORKDIR /app
COPY pyproject.toml ./
COPY src ./src
RUN pip install .

RUN useradd --system --uid 10001 --no-create-home nexa
USER nexa

# Port and every other setting come from NEXA_* environment variables.
EXPOSE 8000
CMD ["nexa-server"]
