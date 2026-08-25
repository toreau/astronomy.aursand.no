FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ .
RUN dotnet restore Astronomy.Api/Astronomy.Api.csproj
RUN dotnet publish Astronomy.Api/Astronomy.Api.csproj -c Release -o /out
RUN dotnet publish Astronomy.DataIngestion/Astronomy.DataIngestion.csproj -c Release -o /out-worker

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS cspice
RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev make tcsh meson ninja-build \
    && rm -rf /var/lib/apt/lists/*
# Pinned to arturania/cspice 53bce32 (2026-08-06): the sed below removes the
# commented prefix_c declaration at SpiceZpr.h:3252-3255, which must match this commit.
RUN git clone --depth 1 --filter=blob:none --sparse https://github.com/arturania/cspice.git /spice-src \
    && cd /spice-src \
    && git fetch --depth 1 origin 53bce326267dd2d6d567de92b15869c9ed7d0629 \
    && git checkout 53bce326267dd2d6d567de92b15869c9ed7d0629 \
    && git sparse-checkout set src include lib
WORKDIR /spice-src/src
# mk_linux.csh hardcodes -m64 (x86-only), which gcc rejects on aarch64.
# Removing it is arch-neutral on x86_64 (gcc defaults to LP64) and required
# for arm64 builds, so the image is multi-arch (amd64 for prod, arm64 for kind).
RUN mkdir -p ../lib ../exe \
    && sed -i '3252,3255d' ../include/SpiceZpr.h \
    && sed -i 's/-m64//g' mk_linux.csh \
    && csh mk_linux.csh
WORKDIR /spice-src
RUN gcc -shared -fPIC -o /libcspice.so \
        -Wl,--whole-archive /spice-src/lib/libcspice.a \
        -Wl,--no-whole-archive -lm

RUN git clone --depth 1 --branch v2.0.1 https://github.com/liberfa/erfa.git /erfa-src \
    && meson setup /erfa/build /erfa-src \
    && ninja -C /erfa/build \
    && find /erfa/build/src -name 'liberfa.so.1*' -exec cp {} /liberfa.so \;

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# .NET SslStream requires libgssapi_krb5.so.2 (GSSAPI) for HTTPS; the minimal
# aspnet runtime image omits it, so install it for outbound HTTPS to work.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
# Run as UID 150 — the same UID Skiperator enforces via pod securityContext,
# so the image's declared user matches the platform (10001 → 150).
RUN useradd --uid 150 --create-home appuser
USER appuser
WORKDIR /app
COPY --from=build /out .
COPY --from=build /out-worker .
COPY --from=cspice /libcspice.so /app/
COPY --from=cspice /liberfa.so /app/
ENV ASPNETCORE_URLS=http://+:8080
ENV ASTRONOMY_KERNEL_PATH=/data/kernels
EXPOSE 8080
ENTRYPOINT ["dotnet", "Astronomy.Api.dll"]
