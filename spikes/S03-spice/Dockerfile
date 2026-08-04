FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev make tcsh meson ninja-build python3 python3-pip \
    && pip3 install --no-cache-dir --break-system-packages skyfield jplephem \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /spice
COPY vendor/cspice /spice/cspice
COPY vendor/erfa /spice/erfa
COPY fixtures/kernels/de440s_a.bsp /spice/kernels/de440s.bsp
COPY vendor/cspice/kernels/lsk/latest_leapseconds.tls /spice/kernels/latest_leapseconds.tls
COPY vendor/cspice/kernels/pck/pck00010.tpc /spice/kernels/pck00010.tpc

WORKDIR /spice/cspice/src
RUN mkdir -p ../lib ../exe \
    && sed -i '3252,3255d' ../include/SpiceZpr.h \
    && csh mk_linux.csh && ls -la /spice/cspice/lib/

WORKDIR /spice
RUN gcc -shared -fPIC -o /spice/libcspice.so \
        -Wl,--whole-archive /spice/cspice/lib/libcspice.a \
        -Wl,--no-whole-archive -lm \
    && ls -la /spice/libcspice.so

WORKDIR /spice/erfa
RUN meson setup build \
    && ninja -C build \
    && ls -la build/src/liberfa.so*

WORKDIR /probe
COPY probe /probe
COPY crosscheck/refgen.py /probe/out/refgen.py
RUN dotnet publish -c Release -o /probe/out \
    && cp /spice/libcspice.so /probe/out/ \
    && cp /spice/erfa/build/src/liberfa.so.1.8.1 /probe/out/liberfa.so

CMD bash -c "dotnet /probe/out/Probe.dll run /spice/kernels/de440s.bsp /spice/kernels/latest_leapseconds.tls /spice/kernels/pck00010.tpc \
    && python3 /probe/out/refgen.py /spice/kernels/de440s.bsp /probe/out/de440s_ref.csv \
    && dotnet /probe/out/Probe.dll crosscheck /probe/out/de440s_ref.csv /spice/kernels/de440s.bsp /spice/kernels/latest_leapseconds.tls /spice/kernels/pck00010.tpc \
    && dotnet /probe/out/Probe.dll erfa /probe/out/de440s_ref.csv /spice/kernels/de440s.bsp /spice/kernels/latest_leapseconds.tls /spice/kernels/pck00010.tpc"
