FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV ASPNETCORE_HTTP_PORTS=
ENV ASPNETCORE_HTTPS_PORTS=
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

