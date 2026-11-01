# Build from the repository root: docker build -f src/Web/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /web
COPY src/Web/package*.json ./
RUN npm ci
COPY src/Web/ ./
RUN npm run build

# Frontend artifacts; the API host serves these in the combined deployment.
FROM scratch AS artifacts
COPY --from=build /web/dist/ /dist/
