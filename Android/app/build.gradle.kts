import org.gradle.api.GradleException
import java.security.MessageDigest
import java.io.File

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

fun sha256Hex(file: File): String {
    val digest = MessageDigest.getInstance("SHA-256")
    file.inputStream().use { input ->
        val buffer = ByteArray(8192)
        while (true) {
            val read = input.read(buffer)
            if (read < 0) break
            if (read > 0) digest.update(buffer, 0, read)
        }
    }
    return digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
}

val releaseStorePath = providers.environmentVariable("GENIAPASSWORD_KEYSTORE").orNull
val releaseStorePassword = providers.environmentVariable("GENIAPASSWORD_KEYSTORE_PASSWORD").orNull
val releaseKeyAlias = providers.environmentVariable("GENIAPASSWORD_KEY_ALIAS").orNull
val releaseKeyPassword = providers.environmentVariable("GENIAPASSWORD_KEY_PASSWORD").orNull
val releaseSigningReady = listOf(
    releaseStorePath,
    releaseStorePassword,
    releaseKeyAlias,
    releaseKeyPassword,
).all { !it.isNullOrBlank() }

android {
    namespace = "com.geniapassword.mobile"
    compileSdk {
        version = release(36) {
            minorApiLevel = 1
        }
    }

    defaultConfig {
        applicationId = "com.geniapassword.mobile"
        minSdk = 26
        targetSdk = 36
        versionCode = 15
        versionName = "1.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (releaseSigningReady) {
            create("geniaRelease") {
                storeFile = file(releaseStorePath!!)
                storePassword = releaseStorePassword
                keyAlias = releaseKeyAlias
                keyPassword = releaseKeyPassword
            }
        }
    }

    buildTypes {
        debug {
            // Test-only build. Never use real production vaults in debuggable APKs.
            isDebuggable = true
        }
        release {
            isDebuggable = false
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
            if (releaseSigningReady) {
                signingConfig = signingConfigs.getByName("geniaRelease")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    buildFeatures {
        compose = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.biometric)
    implementation("org.bouncycastle:bcprov-jdk18on:1.85.2")

    testImplementation(libs.junit)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}

val securityManifestFile = layout.projectDirectory.file("src/main/AndroidManifest.xml")

tasks.register("securitySanityCheck") {
    group = "verification"
    description = "Fail the build if critical offline/security manifest invariants regress."
    inputs.file(securityManifestFile)
    doLast {
        // Use declared task inputs only. Do not access Project/file() from the task action:
        // Gradle 9.x configuration cache cannot serialize build-script Project references.
        val manifestText = inputs.files.singleFile.readText()
        val internetPermission = Regex(
            "<uses-permission[^>]+android:name=\"android\\.permission\\.INTERNET\"",
            RegexOption.IGNORE_CASE,
        )
        if (internetPermission.containsMatchIn(manifestText)) {
            throw GradleException("SECURITY: INTERNET permission must not be present in GeniaPassword.")
        }
        if (!manifestText.contains("android:allowBackup=\"false\"")) {
            throw GradleException("SECURITY: android:allowBackup must remain false.")
        }
        if (!manifestText.contains("android:usesCleartextTraffic=\"false\"")) {
            throw GradleException("SECURITY: cleartext traffic must remain explicitly disabled.")
        }
    }
}

tasks.register("requireReleaseSigning") {
    group = "verification"
    notCompatibleWithConfigurationCache("Release security gate validates external signing state and pinned build files at execution time.")
    description = "Require the permanent GeniaPassword release signing key for release builds."
    doLast {
        if (!releaseSigningReady) {
            throw GradleException(
                "Release signing is not configured. Set GENIAPASSWORD_KEYSTORE, " +
                    "GENIAPASSWORD_KEYSTORE_PASSWORD, GENIAPASSWORD_KEY_ALIAS and GENIAPASSWORD_KEY_PASSWORD."
            )
        }
        val verificationMetadata = rootProject.file("gradle/verification-metadata.xml")
        if (!verificationMetadata.isFile) {
            throw GradleException(
                "Release dependency verification is not pinned. Run PIN_DEPENDENCIES.ps1 on a trusted machine first."
            )
        }
        val wrapperJar = rootProject.file("gradle/wrapper/gradle-wrapper.jar")
        val expectedWrapperSha256 = "7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d"
        if (!wrapperJar.isFile || sha256Hex(wrapperJar) != expectedWrapperSha256) {
            throw GradleException(
                "Gradle 9.7.0 wrapper JAR is not pinned to the official checksum. Run REFRESH_GRADLE_WRAPPER.ps1 first."
            )
        }
    }
}

tasks.matching { it.name == "preReleaseBuild" }.configureEach {
    dependsOn("securitySanityCheck", "requireReleaseSigning")
}

tasks.matching { it.name == "preDebugBuild" }.configureEach {
    dependsOn("securitySanityCheck")
}
