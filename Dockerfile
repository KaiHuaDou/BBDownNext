# syntax=docker/dockerfile:1

# 构建阶段：Alpine 内 AOT 编译 musl 静态产物（与 ci.yml 的 musl 构建步骤一致）
FROM mcr.microsoft.com/dotnet/sdk:9.0.317-alpine3.23 AS build
WORKDIR /src

ARG RID=linux-musl-x64

COPY . .

RUN apk add --no-cache clang build-base zlib-dev protobuf grpc-plugins \
    && export PROTOBUF_PROTOC=/usr/bin/protoc \
    && export GRPC_PROTOC_PLUGIN=/usr/bin/grpc_csharp_plugin \
    && dotnet publish BBDown -r "$RID" -c Release -o /out

# 运行阶段：自带 FFmpeg 用于混流；产物为静态 musl 二进制
FROM alpine:3.23
RUN apk add --no-cache ffmpeg ca-certificates
COPY --from=build /out/BBDown /usr/local/bin/BBDown
ENTRYPOINT ["BBDown"]
