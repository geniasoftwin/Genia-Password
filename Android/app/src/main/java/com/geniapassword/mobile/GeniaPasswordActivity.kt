package com.geniapassword.mobile

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PersistableBundle
import android.os.SystemClock
import android.view.View
import android.view.WindowManager
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import androidx.lifecycle.lifecycleScope
import com.geniapassword.mobile.security.BiometricKeyStore
import com.geniapassword.mobile.ui.GeniaPasswordApp
import com.geniapassword.mobile.ui.theme.GeniaPasswordTheme
import java.util.UUID
import javax.crypto.Cipher
import kotlinx.coroutines.launch

class GeniaPasswordActivity : FragmentActivity() {
    private lateinit var controller: VaultController
    private lateinit var biometricKeys: BiometricKeyStore
    private val handler = Handler(Looper.getMainLooper())
    private var backgroundedAt = 0L
    private var biometricSupported by mutableStateOf(false)
    private var biometricConfigured by mutableStateOf(false)
    private var incomingUri by mutableStateOf<android.net.Uri?>(null)
    private var lastClipboardToken: String? = null
    private val autoLockRunnable = object : Runnable {
        override fun run() {
            if (backgroundedAt <= 0L) return
            if (controller.busy) {
                handler.postDelayed(this, AUTO_LOCK_RETRY_MS)
                return
            }
            if (SystemClock.elapsedRealtime() - backgroundedAt >= AUTO_LOCK_DELAY_MS) {
                lockVault()
                if (!controller.busy) backgroundedAt = 0L
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        window.decorView.setFilterTouchesWhenObscured(true)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            window.decorView.importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            window.decorView.importantForContentCapture = View.IMPORTANT_FOR_CONTENT_CAPTURE_NO_EXCLUDE_DESCENDANTS
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            window.setHideOverlayWindows(true)
        }
        enableEdgeToEdge()

        controller = VaultController(this)
        biometricKeys = BiometricKeyStore(this)
        biometricSupported = biometricKeys.isSupported()
        biometricConfigured = biometricKeys.hasCredential()

        setContent {
            GeniaPasswordTheme {
                GeniaPasswordApp(
                    controller = controller,
                    copyValue = ::copySensitive,
                    biometricSupported = biometricSupported,
                    biometricConfigured = biometricConfigured,
                    onBiometricUnlock = ::unlockWithBiometrics,
                    onEnableBiometric = ::enableBiometrics,
                    onDisableBiometric = ::disableBiometrics,
                    onVaultReplaced = ::resetBiometrics,
                    onLock = ::lockVault,
                    onImportRequest = ::openImportPicker,
                    onExportRequest = ::openExportPicker,
                    initialImportUri = incomingUri,
                    onImportHandled = { incomingUri = null },
                )
            }
        }
    }

    @Deprecated("Deprecated in Android SDK, kept intentionally for low 16-bit request codes on older FragmentActivity versions.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != Activity.RESULT_OK) return

        when (requestCode) {
            REQUEST_IMPORT_VAULT -> data?.data?.let { incomingUri = it }
            REQUEST_EXPORT_VAULT -> data?.data?.let { uri ->
                lifecycleScope.launch { controller.export(uri) }
            }
        }
    }

    private fun openImportPicker() {
        if (controller.busy) return
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            type = "*/*"
            addCategory(Intent.CATEGORY_OPENABLE)
            putExtra(Intent.EXTRA_LOCAL_ONLY, true)
        }
        try {
            @Suppress("DEPRECATION")
            startActivityForResult(Intent.createChooser(intent, "Выбрать vault.pnb"), REQUEST_IMPORT_VAULT)
        } catch (error: Exception) {
            controller.notify("Ошибка запуска проводника: ${error.message}")
        }
    }

    private fun openExportPicker() {
        if (controller.busy) return
        val intent = Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
            type = "application/octet-stream"
            addCategory(Intent.CATEGORY_OPENABLE)
            putExtra(Intent.EXTRA_TITLE, "vault.pnb")
            putExtra(Intent.EXTRA_LOCAL_ONLY, true)
        }
        try {
            @Suppress("DEPRECATION")
            startActivityForResult(intent, REQUEST_EXPORT_VAULT)
        } catch (error: Exception) {
            controller.notify("Ошибка запуска сохранения: ${error.message}")
        }
    }

    override fun onStart() {
        super.onStart()
        handler.removeCallbacks(autoLockRunnable)
        val timedOut = backgroundedAt > 0L &&
            SystemClock.elapsedRealtime() - backgroundedAt >= AUTO_LOCK_DELAY_MS
        if (timedOut) {
            if (controller.busy) {
                // Операцию шифрования не прерываем посередине, но сразу после неё
                // всё равно закрываем vault, потому что минутный таймаут уже истёк.
                handler.postDelayed(autoLockRunnable, AUTO_LOCK_RETRY_MS)
            } else {
                lockVault()
                backgroundedAt = 0L
            }
        } else {
            backgroundedAt = 0L
        }
    }

    override fun onStop() {
        backgroundedAt = SystemClock.elapsedRealtime()
        handler.removeCallbacks(autoLockRunnable)
        handler.postDelayed(autoLockRunnable, AUTO_LOCK_DELAY_MS)
        super.onStop()
    }

    override fun onDestroy() {
        if (isFinishing) clearOwnedClipboard()
        super.onDestroy()
    }

    /**
     * Возвращает true, если приложению стоит показать собственное сообщение о копировании.
     * Android 13+ уже показывает системный clipboard overlay, поэтому Snackbar там не дублируем.
     */
    private fun copySensitive(label: String, value: String): Boolean {
        if (value.isEmpty()) return false
        // Android 8/9 allowed background apps to observe the global clipboard. Do not place
        // passwords there at all; logins remain available for legacy-device usability.
        if (label.equals("Пароль", ignoreCase = true) && Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
            controller.notify("На Android 8/9 копирование пароля отключено: системный буфер обмена может читаться фоновыми приложениями.")
            return false
        }
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val token = UUID.randomUUID().toString()
        val clip = ClipData.newPlainText(label, value)
        clip.description.extras = PersistableBundle().apply {
            putString(CLIP_TOKEN_KEY, token)
            // Use the compatibility key on every supported Android version. Android 13+ uses
            // the same key for the system clipboard preview; older versions safely ignore it.
            putBoolean(CLIP_SENSITIVE_KEY, true)
        }
        clipboard.setPrimaryClip(clip)
        lastClipboardToken = token

        handler.postDelayed({ clearClipboardIfTokenMatches(token) }, CLIPBOARD_CLEAR_DELAY_MS)
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU
    }

    private fun clearClipboardIfTokenMatches(token: String) {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val currentToken = clipboard.primaryClipDescription?.extras?.getString(CLIP_TOKEN_KEY)
        if (currentToken == token) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                clipboard.clearPrimaryClip()
            } else {
                clipboard.setPrimaryClip(ClipData.newPlainText("", ""))
            }
            if (lastClipboardToken == token) lastClipboardToken = null
        }
    }

    private fun clearOwnedClipboard() {
        val token = lastClipboardToken ?: return
        clearClipboardIfTokenMatches(token)
        lastClipboardToken = null
    }

    private fun lockVault() {
        clearOwnedClipboard()
        controller.lock()
    }

    private fun enableBiometrics() {
        if (controller.busy) return
        val unlockKey = controller.currentUnlockKeyCopy() ?: return
        val cipher = try {
            biometricKeys.prepareEnrollmentCipher()
        } catch (error: Exception) {
            unlockKey.fill(0)
            controller.notify(error.message ?: "Не удалось подготовить биометрию.")
            return
        }

        showBiometricPrompt(
            title = "Включение биометрии",
            subtitle = "Подтвердите отпечаток или распознавание лица",
            negativeButton = "Отмена",
            cipher = cipher,
            onSuccess = { authenticatedCipher ->
                try {
                    biometricKeys.completeEnrollment(authenticatedCipher, unlockKey)
                    biometricConfigured = true
                    controller.notify("Вход по биометрии включён")
                } catch (error: Exception) {
                    biometricKeys.clear()
                    biometricConfigured = false
                    controller.notify(error.message ?: "Не удалось включить биометрию.")
                } finally {
                    unlockKey.fill(0)
                }
            },
            onCancel = {
                unlockKey.fill(0)
                biometricKeys.clear()
                biometricConfigured = false
            },
        )
    }

    private fun unlockWithBiometrics() {
        if (controller.busy) return
        val cipher = try {
            biometricKeys.prepareUnlockCipher()
        } catch (error: Exception) {
            resetBiometrics()
            controller.notify("Биометрический ключ недоступен. Введите мастер-пароль ещё раз.")
            return
        }

        showBiometricPrompt(
            title = "Открыть GeniaPassword",
            subtitle = "Подтвердите биометрию",
            negativeButton = "Использовать пароль",
            cipher = cipher,
            onSuccess = unlock@{ authenticatedCipher ->
                val unlockKey = try {
                    biometricKeys.unwrapUnlockKey(authenticatedCipher)
                } catch (error: Exception) {
                    resetBiometrics()
                    controller.notify("Биометрический ключ повреждён. Введите мастер-пароль.")
                    return@unlock
                }
                lifecycleScope.launch {
                    try {
                        if (!controller.unlockWithUnlockKey(unlockKey)) {
                            resetBiometrics()
                            controller.notify("Хранилище изменилось. Введите мастер-пароль и включите биометрию заново.")
                        }
                    } finally {
                        unlockKey.fill(0)
                    }
                }
            },
            onCancel = {},
        )
    }

    private fun disableBiometrics() {
        resetBiometrics()
        controller.notify("Вход по биометрии отключён")
    }

    private fun resetBiometrics() {
        biometricKeys.clear()
        biometricConfigured = false
    }

    private fun showBiometricPrompt(
        title: String,
        subtitle: String,
        negativeButton: String,
        cipher: Cipher,
        onSuccess: (Cipher) -> Unit,
        onCancel: () -> Unit,
    ) {
        val prompt = BiometricPrompt(
            this,
            ContextCompat.getMainExecutor(this),
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                    val authenticatedCipher = result.cryptoObject?.cipher
                    if (authenticatedCipher == null) {
                        onCancel()
                        controller.notify("Android не вернул криптографический ключ.")
                    } else {
                        onSuccess(authenticatedCipher)
                    }
                }

                override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                    onCancel()
                    if (errorCode != BiometricPrompt.ERROR_NEGATIVE_BUTTON &&
                        errorCode != BiometricPrompt.ERROR_USER_CANCELED &&
                        errorCode != BiometricPrompt.ERROR_CANCELED
                    ) {
                        controller.notify(errString.toString())
                    }
                }
            },
        )
        try {
            val promptInfo = BiometricPrompt.PromptInfo.Builder()
                .setTitle(title)
                .setSubtitle(subtitle)
                .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
                .setNegativeButtonText(negativeButton)
                .build()
            prompt.authenticate(promptInfo, BiometricPrompt.CryptoObject(cipher))
        } catch (error: Exception) {
            onCancel()
            controller.notify("Не удалось открыть биометрию: ${error.message ?: "ошибка Android"}")
        }
    }

    private companion object {
        const val AUTO_LOCK_DELAY_MS = 60_000L
        const val AUTO_LOCK_RETRY_MS = 1_000L
        const val CLIPBOARD_CLEAR_DELAY_MS = 30_000L
        const val CLIP_TOKEN_KEY = "com.geniapassword.mobile.CLIP_TOKEN"
        const val CLIP_SENSITIVE_KEY = "android.content.extra.IS_SENSITIVE"
        const val REQUEST_IMPORT_VAULT = 0x1201
        const val REQUEST_EXPORT_VAULT = 0x1202
    }
}
