# Build image for the signed release APK: JDK 21 + Node 22 + Android SDK 36.
# SDK is world-writable so Gradle can run as the host user without root.
FROM eclipse-temurin:21-jdk

RUN apt-get update && apt-get install -y --no-install-recommends curl unzip ca-certificates \
 && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

ENV ANDROID_HOME=/opt/android-sdk
RUN mkdir -p $ANDROID_HOME/cmdline-tools \
 && curl -fsSL https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip -o /tmp/cli.zip \
 && unzip -q /tmp/cli.zip -d $ANDROID_HOME/cmdline-tools && rm /tmp/cli.zip \
 && mv $ANDROID_HOME/cmdline-tools/cmdline-tools $ANDROID_HOME/cmdline-tools/latest \
 && yes | $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager --licenses >/dev/null \
 && $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager "platform-tools" "platforms;android-36" "build-tools;36.0.0" \
 && chmod -R a+rwX $ANDROID_HOME

WORKDIR /work
