FROM bitnami/minideb

COPY Server.zip Server.zip

RUN apt-get update && apt-get install \
    && apt-get install -y unzip wget \
    && wget https://downloads.tuxfamily.org/godotengine/3.5/mono/Godot_v3.5-stable_mono_linux_server_64.zip \
    && unzip Godot_v3.5-stable_mono_linux_server_64.zip \
    && rm -f Godot_v3.5-stable_mono_linux_server_64.zip \
    && unzip Server.zip \
    && rm Server.zip \
    && mv -v Server/* Godot_v3.5-stable_mono_linux_server_64/ \
    && rm -d Server

WORKDIR /Godot_v3.5-stable_mono_linux_server_64

EXPOSE 6575/udp

ENTRYPOINT ["./Godot_v3.5-stable_mono_linux_server.64", "--main-pack", "server.pck"]
