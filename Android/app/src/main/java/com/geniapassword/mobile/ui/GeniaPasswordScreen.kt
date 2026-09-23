package com.geniapassword.mobile.ui

import android.net.Uri
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentHeight
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.geniapassword.mobile.AccessState
import com.geniapassword.mobile.R
import com.geniapassword.mobile.VaultController
import com.geniapassword.mobile.data.CustomField
import com.geniapassword.mobile.data.VaultConstraints
import com.geniapassword.mobile.data.VaultEntry
import com.geniapassword.mobile.ui.theme.GeniaGreen
import com.geniapassword.mobile.ui.theme.GeniaGreenSoft
import com.geniapassword.mobile.ui.theme.GeniaRedSoft
import java.time.Duration
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.launch

private const val BUILD_LABEL = "1.0 Final · Offline"
private const val MAX_MASTER_PASSWORD_CHARS = 1_024

@Composable
fun GeniaPasswordApp(
    controller: VaultController,
    copyValue: (String, String) -> Boolean,
    biometricSupported: Boolean,
    biometricConfigured: Boolean,
    onBiometricUnlock: () -> Unit,
    onEnableBiometric: () -> Unit,
    onDisableBiometric: () -> Unit,
    onVaultReplaced: () -> Unit,
    onLock: () -> Unit,
    onImportRequest: () -> Unit,
    onExportRequest: () -> Unit,
    initialImportUri: Uri? = null,
    onImportHandled: () -> Unit = {},
) {
    val snackbar = remember { SnackbarHostState() }
    var pendingImportUri by remember { mutableStateOf<Uri?>(null) }
    var biometricOfferDismissed by remember { mutableStateOf(false) }

    LaunchedEffect(initialImportUri) {
        if (initialImportUri != null) {
            pendingImportUri = initialImportUri
            onImportHandled()
        }
    }

    val message = controller.message
    LaunchedEffect(message) {
        if (!message.isNullOrBlank()) {
            snackbar.showSnackbar(message)
            controller.consumeMessage()
        }
    }

    when (controller.accessState) {
        AccessState.NeedsCreation -> AccessScreen(
            creating = true,
            snackbar = snackbar,
            biometricAvailable = false,
            biometricSupported = biometricSupported,
            busy = controller.busy,
            onSubmit = { password, confirmation ->
                controller.create(password, confirmation).also { created ->
                    if (created) onVaultReplaced()
                }
            },
            onBiometricUnlock = {},
        )

        AccessState.Locked -> AccessScreen(
            creating = false,
            snackbar = snackbar,
            biometricAvailable = biometricSupported && biometricConfigured,
            biometricSupported = biometricSupported,
            busy = controller.busy,
            onSubmit = { password, _ -> controller.unlock(password) },
            onBiometricUnlock = onBiometricUnlock,
        )

        is AccessState.Open -> MainScreen(
            controller = controller,
            snackbar = snackbar,
            copyValue = copyValue,
            onLock = onLock,
            onImport = onImportRequest,
            onExport = onExportRequest,
            biometricSupported = biometricSupported,
            biometricConfigured = biometricConfigured,
            onEnableBiometric = {
                biometricOfferDismissed = true
                onEnableBiometric()
            },
            onDisableBiometric = {
                biometricOfferDismissed = true
                onDisableBiometric()
            },
        )
    }

    if (
        controller.accessState is AccessState.Open &&
        biometricSupported &&
        !biometricConfigured &&
        !biometricOfferDismissed
    ) {
        AlertDialog(
            onDismissRequest = { biometricOfferDismissed = true },
            title = { Text("Быстрый вход по биометрии") },
            text = {
                Text(
                    "Можно открывать хранилище отпечатком или распознаванием лица. " +
                        "Мастер-пароль по-прежнему останется доступен.",
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        biometricOfferDismissed = true
                        onEnableBiometric()
                    },
                ) { Text("Включить") }
            },
            dismissButton = {
                TextButton(onClick = { biometricOfferDismissed = true }) { Text("Позже") }
            },
        )
    }

    if (controller.accessState is AccessState.Open) {
        pendingImportUri?.let { uri ->
            ImportPasswordDialog(
                busy = controller.busy,
                onDismiss = { if (!controller.busy) pendingImportUri = null },
                onImport = { password ->
                    val error = controller.importVault(uri, password)
                    if (error == null) {
                        onVaultReplaced()
                        biometricOfferDismissed = false
                        pendingImportUri = null
                    }
                    error
                },
            )
        }
    }
}

@Composable
private fun AccessScreen(
    creating: Boolean,
    snackbar: SnackbarHostState,
    biometricAvailable: Boolean,
    biometricSupported: Boolean,
    busy: Boolean,
    onSubmit: suspend (String, String) -> Boolean,
    onBiometricUnlock: () -> Unit,
) {
    var password by remember { mutableStateOf("") }
    var confirmation by remember { mutableStateOf("") }
    var showPassword by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        containerColor = MaterialTheme.colorScheme.background,
    ) { padding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(MaterialTheme.colorScheme.background),
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 22.dp, vertical = 18.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                BrandHeader()
                if (busy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())

                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(24.dp),
                    color = MaterialTheme.colorScheme.surface,
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.8f)),
                ) {
                    Column(
                        modifier = Modifier.padding(18.dp),
                        verticalArrangement = Arrangement.spacedBy(13.dp),
                    ) {
                        Text(
                            text = if (creating) "Создание хранилища" else "Добро пожаловать",
                            style = MaterialTheme.typography.titleLarge,
                        )
                        Text(
                            text = if (creating) {
                                "Создайте мастер-пароль. Все записи будут храниться локально и в зашифрованном виде."
                            } else if (biometricAvailable) {
                                "Введите мастер-пароль или используйте биометрию."
                            } else if (biometricSupported) {
                                "Введите мастер-пароль. Биометрию можно включить после входа."
                            } else {
                                "Введите мастер-пароль, чтобы открыть записи."
                            },
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )

                        CompactField(
                            value = password,
                            onValueChange = { if (it.length <= MAX_MASTER_PASSWORD_CHARS) password = it },
                            label = "Мастер-пароль",
                            modifier = Modifier
                                .fillMaxWidth(0.94f)
                                .align(Alignment.CenterHorizontally),
                            keyboardType = KeyboardType.Password,
                            imeAction = if (creating) ImeAction.Next else ImeAction.Done,
                            visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                            minHeight = 46.dp,
                            trailing = {
                                TinyTextButton(
                                    text = if (showPassword) "Скрыть" else "Показать",
                                    onClick = { showPassword = !showPassword },
                                )
                            },
                        )

                        if (creating) {
                            CompactField(
                                value = confirmation,
                                onValueChange = { if (it.length <= MAX_MASTER_PASSWORD_CHARS) confirmation = it },
                                label = "Повторите мастер-пароль",
                                modifier = Modifier
                                    .fillMaxWidth(0.94f)
                                    .align(Alignment.CenterHorizontally),
                                keyboardType = KeyboardType.Password,
                                imeAction = ImeAction.Done,
                                visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                                minHeight = 46.dp,
                                )
                        }

                        Button(
                            onClick = {
                                scope.launch {
                                    if (onSubmit(password, confirmation)) {
                                        password = ""
                                        confirmation = ""
                                        showPassword = false
                                    }
                                }
                            },
                            enabled = !busy && password.isNotBlank() && (!creating || confirmation.isNotBlank()),
                            modifier = Modifier
                                .fillMaxWidth(0.94f)
                                .align(Alignment.CenterHorizontally)
                                .height(48.dp),
                            shape = RoundedCornerShape(14.dp),
                        ) {
                            Text(if (busy) "Подождите…" else if (creating) "Создать хранилище" else "Открыть")
                        }

                        if (!creating && biometricAvailable) {
                            OutlinedButton(
                                onClick = onBiometricUnlock,
                                enabled = !busy,
                                modifier = Modifier
                                    .fillMaxWidth(0.94f)
                                    .align(Alignment.CenterHorizontally)
                                    .height(46.dp),
                                shape = RoundedCornerShape(14.dp),
                                border = BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.35f)),
                            ) {
                                Text("Открыть по биометрии")
                            }
                        }
                    }
                }

                SecurityNote(
                    title = "Полностью офлайн",
                    text = "Нет аккаунта, облака, телеметрии и разрешения INTERNET. Vault остаётся только у вас.",
                )
            }
        }
    }
}

@Composable
private fun BrandHeader() {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Row(
            modifier = Modifier.weight(1f),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Image(
                painter = painterResource(R.drawable.ic_brand_g_lock),
                contentDescription = "GeniaPassword",
                modifier = Modifier
                    .size(44.dp)
                    .clip(RoundedCornerShape(14.dp)),
            )
            Spacer(Modifier.size(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = "GeniaPassword",
                    modifier = Modifier.fillMaxWidth(),
                    style = MaterialTheme.typography.titleMedium.copy(
                        fontSize = 18.sp,
                        lineHeight = 20.sp,
                    ),
                    maxLines = 1,
                    softWrap = false,
                    overflow = TextOverflow.Clip,
                )
                Text(
                    "Локальное защищённое хранилище",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    softWrap = false,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun VersionBadge() {
    Surface(
        shape = RoundedCornerShape(999.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
    ) {
        Text(
            text = BUILD_LABEL,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.primary,
            maxLines = 1,
        )
    }
}

@Composable
private fun MainScreen(
    controller: VaultController,
    snackbar: SnackbarHostState,
    copyValue: (String, String) -> Boolean,
    onLock: () -> Unit,
    onImport: () -> Unit,
    onExport: () -> Unit,
    biometricSupported: Boolean,
    biometricConfigured: Boolean,
    onEnableBiometric: () -> Unit,
    onDisableBiometric: () -> Unit,
) {
    var query by remember { mutableStateOf("") }
    var editingEntry by remember { mutableStateOf<VaultEntry?>(null) }
    var creatingEntry by remember { mutableStateOf(false) }
    var deletingEntry by remember { mutableStateOf<VaultEntry?>(null) }
    var showSettings by remember { mutableStateOf(false) }
    var showAudit by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    // Reading this state is intentional: repository entries are a normal MutableList,
    // and controller.revision is the explicit Compose invalidation signal after saves.
    val uiRevision = controller.revision
    val allEntries = controller.entries
    val filteredEntries = remember(query, uiRevision, allEntries.size) {
        val terms = query
            .trim()
            .lowercase()
            .split(Regex("\\s+"))
            .filter { it.isNotBlank() }

        allEntries
            .filter { entry ->
                terms.all { term ->
                    entry.title.lowercase().contains(term) ||
                        entry.userName.lowercase().contains(term) ||
                        entry.website.lowercase().contains(term) ||
                        entry.notes.lowercase().contains(term) ||
                        entry.customFields.any { field ->
                            field.name.lowercase().contains(term) ||
                                (!field.isSecret && field.value.lowercase().contains(term))
                        }
                }
            }
            .sortedBy { it.title.lowercase() }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        containerColor = MaterialTheme.colorScheme.background,
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
        ) {
            Surface(
                color = MaterialTheme.colorScheme.surface,
                border = BorderStroke(
                    width = 0.5.dp,
                    color = MaterialTheme.colorScheme.outline.copy(alpha = 0.7f),
                ),
            ) {
                Column(
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 9.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Row(
                            modifier = Modifier.weight(1f),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Image(
                                painter = painterResource(R.drawable.ic_brand_g_lock),
                                contentDescription = "GeniaPassword",
                                modifier = Modifier
                                    .size(30.dp)
                                    .clip(RoundedCornerShape(10.dp)),
                            )
                            Spacer(Modifier.size(7.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = "GeniaPassword",
                                    modifier = Modifier.fillMaxWidth(),
                                    style = MaterialTheme.typography.titleMedium.copy(
                                        fontSize = 16.sp,
                                        lineHeight = 18.sp,
                                    ),
                                    maxLines = 1,
                                    softWrap = false,
                                    overflow = TextOverflow.Clip,
                                )
                                Text(
                                    "${allEntries.size} записей",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 1,
                                )
                            }
                        }
                        OutlinedButton(
                            onClick = { showSettings = true },
                            modifier = Modifier.height(34.dp),
                            shape = RoundedCornerShape(10.dp),
                            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 9.dp),
                        ) {
                            Icon(
                                painter = painterResource(R.drawable.ic_settings),
                                contentDescription = null,
                                modifier = Modifier.size(14.dp),
                            )
                            Spacer(Modifier.size(4.dp))
                            Text("Управление", fontSize = 12.sp, maxLines = 1)
                        }
                    }

                    if (controller.busy) LinearProgressIndicator(modifier = Modifier.fillMaxWidth())

                    CompactField(
                        value = query,
                        onValueChange = { query = it },
                        label = null,
                        placeholder = "Поиск в хранилище…",
                        imeAction = ImeAction.Search,
                        minHeight = 42.dp,
                        leading = {
                            Icon(
                                painter = painterResource(R.drawable.ic_search),
                                contentDescription = null,
                                modifier = Modifier.size(16.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        },
                        trailing = if (query.isNotEmpty()) {
                            { TinyTextButton("Очистить", { query = "" }) }
                        } else null,
                    )

                    Button(
                        onClick = { creatingEntry = true },
                        enabled = !controller.busy,
                        modifier = Modifier.height(36.dp),
                        shape = RoundedCornerShape(10.dp),
                        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 11.dp),
                    ) {
                        Icon(painterResource(R.drawable.ic_add), null, Modifier.size(14.dp))
                        Spacer(Modifier.size(4.dp))
                        Text("Новая запись", fontSize = 12.5.sp, maxLines = 1)
                    }
                }
            }

            if (filteredEntries.isEmpty()) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(24.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Surface(
                        shape = RoundedCornerShape(22.dp),
                        color = MaterialTheme.colorScheme.surface,
                        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.7f)),
                    ) {
                        Column(
                            modifier = Modifier.padding(22.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(6.dp),
                        ) {
                            Text(
                                if (query.isBlank()) "Пока нет записей" else "Ничего не найдено",
                                style = MaterialTheme.typography.titleMedium,
                            )
                            Text(
                                if (query.isBlank()) "Создайте первую запись или импортируйте хранилище через «Управление»."
                                else "Попробуйте изменить строку поиска.",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(
                        start = 12.dp,
                        end = 12.dp,
                        top = 12.dp,
                        bottom = 24.dp,
                    ),
                    verticalArrangement = Arrangement.spacedBy(9.dp),
                ) {
                    items(filteredEntries, key = { it.id }) { entry ->
                        EntryCard(
                            entry = entry,
                            onOpen = { editingEntry = entry },
                            onCopyLogin = {
                                if (entry.userName.isBlank()) {
                                    controller.notify("В этой записи логин не заполнен")
                                } else if (copyValue("Логин", entry.userName)) {
                                    scope.launch { snackbar.showSnackbar("Логин скопирован") }
                                }
                            },
                            onCopyPassword = {
                                if (entry.password.isBlank()) {
                                    controller.notify("В этой записи пароль не заполнен")
                                } else if (copyValue("Пароль", entry.password)) {
                                    scope.launch { snackbar.showSnackbar("Пароль скопирован · очистка через 30 сек.") }
                                }
                            },
                            onDelete = { deletingEntry = entry },
                        )
                    }
                }
            }
        }
    }

    if (creatingEntry) {
        EntryEditorDialog(
            controller = controller,
            entry = null,
            busy = controller.busy,
            onDismiss = { if (!controller.busy) creatingEntry = false },
            onSave = { candidate ->
                if (controller.saveEntry(candidate)) {
                    creatingEntry = false
                    null
                } else {
                    controller.takeMessage() ?: "Не удалось сохранить запись."
                }
            },
        )
    }

    editingEntry?.let { entry ->
        EntryEditorDialog(
            controller = controller,
            entry = entry,
            busy = controller.busy,
            onDismiss = { if (!controller.busy) editingEntry = null },
            onSave = { candidate ->
                if (controller.saveEntry(candidate)) {
                    editingEntry = null
                    null
                } else {
                    controller.takeMessage() ?: "Не удалось сохранить запись."
                }
            },
        )
    }

    deletingEntry?.let { entry ->
        AlertDialog(
            onDismissRequest = { if (!controller.busy) deletingEntry = null },
            title = { Text("Удалить запись?") },
            text = {
                Text("«${entry.title.ifBlank { "Без названия" }}» будет удалена из текущего vault.")
            },
            confirmButton = {
                Button(
                    onClick = {
                        scope.launch {
                            if (controller.deleteEntry(entry.id)) deletingEntry = null
                        }
                    },
                    enabled = !controller.busy,
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                ) { Text("Удалить") }
            },
            dismissButton = {
                TextButton(onClick = { deletingEntry = null }, enabled = !controller.busy) { Text("Отмена") }
            },
        )
    }

    if (showSettings) {
        VaultManagementDialog(
            busy = controller.busy,
            biometricSupported = biometricSupported,
            biometricConfigured = biometricConfigured,
            vaultRevision = controller.currentVaultRevision,
            backupCount = controller.localBackupCount(),
            entryCount = allEntries.size,
            onDismiss = { if (!controller.busy) showSettings = false },
            onImport = {
                showSettings = false
                onImport()
            },
            onExport = {
                showSettings = false
                onExport()
            },
            onSecurityAudit = {
                showSettings = false
                showAudit = true
            },
            onEnableBiometric = onEnableBiometric,
            onDisableBiometric = onDisableBiometric,
            onLock = {
                showSettings = false
                onLock()
            },
        )
    }

    if (showAudit) {
        SecurityAuditDialog(
            entries = allEntries,
            onDismiss = { showAudit = false },
        )
    }
}

@Composable
private fun EntryCard(
    entry: VaultEntry,
    onOpen: () -> Unit,
    onCopyLogin: () -> Unit,
    onCopyPassword: () -> Unit,
    onDelete: () -> Unit,
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(18.dp))
            .clickable(onClick = onOpen),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.72f)),
    ) {
        Column(
            modifier = Modifier.padding(horizontal = 13.dp, vertical = 11.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.Top,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = entry.title.ifBlank { "Без названия" },
                        style = MaterialTheme.typography.titleSmall,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    if (entry.userName.isNotBlank()) {
                        Text(
                            text = entry.userName,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                Text(
                    "Открыть",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.primary,
                    maxLines = 1,
                )
            }

            if (entry.website.isNotBlank()) {
                Text(
                    text = entry.website,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                EntryActionButton(
                    iconRes = R.drawable.ic_user,
                    text = "Логин",
                    onClick = onCopyLogin,
                    modifier = Modifier.weight(1f),
                )
                EntryActionButton(
                    iconRes = R.drawable.ic_key,
                    text = "Пароль",
                    onClick = onCopyPassword,
                    modifier = Modifier.weight(1f),
                )
                EntryIconButton(
                    iconRes = R.drawable.ic_edit,
                    contentDescription = "Редактировать",
                    onClick = onOpen,
                )
                EntryIconButton(
                    iconRes = R.drawable.ic_delete,
                    contentDescription = "Удалить",
                    onClick = onDelete,
                    contentColor = MaterialTheme.colorScheme.error,
                    containerColor = MaterialTheme.colorScheme.errorContainer.copy(alpha = 0.72f),
                )
            }
        }
    }
}

@Composable
private fun EntryEditorDialog(
    controller: VaultController,
    entry: VaultEntry?,
    busy: Boolean,
    onDismiss: () -> Unit,
    onSave: suspend (VaultEntry) -> String?,
) {
    var title by remember(entry?.id) { mutableStateOf(entry?.title.orEmpty()) }
    var website by remember(entry?.id) { mutableStateOf(entry?.website.orEmpty()) }
    var userName by remember(entry?.id) { mutableStateOf(entry?.userName.orEmpty()) }
    var password by remember(entry?.id) { mutableStateOf(entry?.password.orEmpty()) }
    var notes by remember(entry?.id) { mutableStateOf(entry?.notes.orEmpty()) }
    var fields by remember(entry?.id) { mutableStateOf(entry?.customFields.orEmpty()) }
    var showPassword by remember(entry?.id) { mutableStateOf(false) }
    var confirmRegeneration by remember(entry?.id) { mutableStateOf(false) }
    var showGenerator by remember(entry?.id) { mutableStateOf(false) }
    var validationError by remember(entry?.id) { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    Dialog(
        onDismissRequest = { if (!busy) onDismiss() },
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 20.dp)
                .heightIn(max = 760.dp),
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Column(modifier = Modifier.fillMaxWidth()) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 13.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            if (entry == null) "Новая запись" else "Редактирование",
                            style = MaterialTheme.typography.titleMedium,
                        )
                        Text(
                            if (entry == null) "Заполните только нужные поля" else entry.title.ifBlank { "Без названия" },
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    TinyTextButton("Закрыть", onDismiss)
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.65f))

                Column(
                    modifier = Modifier
                        .weight(1f, fill = false)
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(11.dp),
                ) {
                    CompactField(
                        value = title,
                        onValueChange = { if (it.length <= VaultConstraints.MAX_TITLE_CHARS) title = it },
                        label = "Название*",
                        imeAction = ImeAction.Next,
                    )
                    CompactField(
                        value = userName,
                        onValueChange = { if (it.length <= VaultConstraints.MAX_USERNAME_CHARS) userName = it },
                        label = "Логин",
                        imeAction = ImeAction.Next,
                    )
                    CompactField(
                        value = password,
                        onValueChange = { if (it.length <= VaultConstraints.MAX_PASSWORD_CHARS) password = it },
                        label = "Пароль",
                        keyboardType = KeyboardType.Password,
                        imeAction = ImeAction.Next,
                        visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                    )
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        SmallSoftButton(
                            text = if (showPassword) "Скрыть" else "Показать",
                            onClick = { showPassword = !showPassword },
                            modifier = Modifier.weight(1f),
                        )
                        SmallSoftButton(
                            text = "Сгенерировать",
                            onClick = {
                                if (entry != null && password.isNotBlank()) confirmRegeneration = true
                                else password = controller.generatePassword()
                            },
                            modifier = Modifier.weight(1.35f),
                        )
                        SmallSoftButton(
                            text = "Настроить",
                            onClick = { showGenerator = true },
                            modifier = Modifier.weight(1f),
                        )
                    }
                    CompactField(
                        value = website,
                        onValueChange = { if (it.length <= VaultConstraints.MAX_WEBSITE_CHARS) website = it },
                        label = "Сайт",
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Next,
                    )
                    CompactField(
                        value = notes,
                        onValueChange = { if (it.length <= VaultConstraints.MAX_NOTES_CHARS) notes = it },
                        label = "Заметки",
                        singleLine = false,
                        minHeight = 88.dp,
                        imeAction = ImeAction.Default,
                    )

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text("Дополнительные поля", style = MaterialTheme.typography.titleSmall)
                        TextButton(
                            onClick = {
                                if (fields.size < VaultConstraints.MAX_CUSTOM_FIELDS_PER_ENTRY) {
                                    fields = fields + CustomField()
                                }
                            },
                            enabled = fields.size < VaultConstraints.MAX_CUSTOM_FIELDS_PER_ENTRY,
                        ) {
                            Text("+ Добавить", fontSize = 13.sp)
                        }
                    }

                    fields.forEachIndexed { index, field ->
                        CustomFieldEditor(
                            field = field,
                            onChange = { replacement ->
                                fields = fields.toMutableList().also { it[index] = replacement }
                            },
                            onRemove = {
                                fields = fields.toMutableList().also { it.removeAt(index) }
                            },
                        )
                    }

                    validationError?.let { error ->
                        Surface(
                            shape = RoundedCornerShape(12.dp),
                            color = GeniaRedSoft,
                        ) {
                            Text(
                                error,
                                modifier = Modifier.padding(10.dp),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.error,
                            )
                        }
                    }
                }

                HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.65f))
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(12.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    OutlinedButton(
                        onClick = onDismiss,
                        enabled = !busy,
                        modifier = Modifier
                            .weight(1f)
                            .height(42.dp),
                        shape = RoundedCornerShape(12.dp),
                    ) {
                        Text("Отмена", fontSize = 13.sp)
                    }
                    Button(
                        onClick = {
                            validationError = validateEntryInput(title, website, userName, password, notes, fields)
                            if (validationError == null) {
                                val candidate = VaultEntry(
                                    id = entry?.id ?: UUID.randomUUID().toString(),
                                    title = title.trim(),
                                    website = website.trim(),
                                    userName = userName,
                                    password = password,
                                    notes = notes,
                                    customFields = fields.filter { it.name.isNotBlank() || it.value.isNotBlank() },
                                    updatedUtc = entry?.updatedUtc ?: Instant.now().toString(),
                                    passwordUpdatedUtc = entry?.passwordUpdatedUtc ?: Instant.now().toString(),
                                    passwordHistoryJson = entry?.passwordHistoryJson,
                                    unknownJson = entry?.unknownJson,
                                )
                                scope.launch { validationError = onSave(candidate) }
                            }
                        },
                        enabled = !busy,
                        modifier = Modifier
                            .weight(1.35f)
                            .height(42.dp),
                        shape = RoundedCornerShape(12.dp),
                    ) {
                        Text(if (busy) "Сохранение…" else "Сохранить", fontSize = 13.sp)
                    }
                }
            }
        }
    }

    if (confirmRegeneration) {
        AlertDialog(
            onDismissRequest = { confirmRegeneration = false },
            title = { Text("Заменить текущий пароль?") },
            text = {
                Text(
                    "Будет создан новый пароль вместо текущего значения в форме. " +
                        "В vault изменение попадёт только после нажатия «Сохранить».",
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        password = controller.generatePassword()
                        confirmRegeneration = false
                    },
                ) { Text("Сгенерировать") }
            },
            dismissButton = {
                TextButton(onClick = { confirmRegeneration = false }) { Text("Отмена") }
            },
        )
    }

    if (showGenerator) {
        PasswordGeneratorDialog(
            controller = controller,
            onDismiss = { showGenerator = false },
            onUse = { generated ->
                password = generated
                showGenerator = false
            },
        )
    }
}

@Composable
private fun CustomFieldEditor(
    field: CustomField,
    onChange: (CustomField) -> Unit,
    onRemove: () -> Unit,
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceVariant,
    ) {
        Column(
            modifier = Modifier.padding(10.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            CompactField(
                value = field.name,
                onValueChange = {
                    if (it.length <= VaultConstraints.MAX_FIELD_NAME_CHARS) onChange(field.copy(name = it))
                },
                label = "Название поля",
            )
            CompactField(
                value = field.value,
                onValueChange = {
                    if (it.length <= VaultConstraints.MAX_FIELD_VALUE_CHARS) onChange(field.copy(value = it))
                },
                label = "Значение",
                keyboardType = if (field.isSecret) KeyboardType.Password else KeyboardType.Text,
                visualTransformation = if (field.isSecret) PasswordVisualTransformation() else VisualTransformation.None,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(
                        checked = field.isSecret,
                        onCheckedChange = { onChange(field.copy(isSecret = it)) },
                    )
                    Text("Секрет", style = MaterialTheme.typography.bodySmall)
                }
                TinyTextButton("Удалить", onRemove, color = MaterialTheme.colorScheme.error)
            }
        }
    }
}

@Composable
private fun VaultManagementDialog(
    busy: Boolean,
    biometricSupported: Boolean,
    biometricConfigured: Boolean,
    vaultRevision: Long?,
    backupCount: Int,
    entryCount: Int,
    onDismiss: () -> Unit,
    onImport: () -> Unit,
    onExport: () -> Unit,
    onSecurityAudit: () -> Unit,
    onEnableBiometric: () -> Unit,
    onDisableBiometric: () -> Unit,
    onLock: () -> Unit,
) {
    Dialog(
        onDismissRequest = { if (!busy) onDismiss() },
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp),
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Column(
                modifier = Modifier
                    .verticalScroll(rememberScrollState())
                    .padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Column {
                        Text("Управление", style = MaterialTheme.typography.titleLarge)
                        Text(
                            BUILD_LABEL,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                    TinyTextButton("Закрыть", onDismiss)
                }

                SectionTitle("Хранилище")
                SettingsInfo(
                    title = "Состояние vault",
                    subtitle = "Vault v2 · revision ${vaultRevision ?: 0} · $entryCount записей · $backupCount локальных резервных копий",
                )
                SettingsAction(
                    title = "Импорт хранилища",
                    subtitle = "Desktop Vault v2 / Argon2id поддерживается",
                    onClick = onImport,
                    enabled = !busy,
                )
                SettingsAction(
                    title = "Экспорт хранилища",
                    subtitle = "Сохранить зашифрованную переносимую копию",
                    onClick = onExport,
                    enabled = !busy,
                )

                SectionTitle("Безопасность")
                SettingsAction(
                    title = "Аудит безопасности",
                    subtitle = "Слабые, повторяющиеся и давно не менявшиеся пароли — полностью офлайн",
                    onClick = onSecurityAudit,
                    enabled = !busy,
                )
                if (biometricSupported) {
                    SettingsAction(
                        title = if (biometricConfigured) "Биометрия включена" else "Включить биометрию",
                        subtitle = if (biometricConfigured) {
                            "Отключить быстрый вход на этом устройстве"
                        } else {
                            "Использовать сильную биометрию Android для входа"
                        },
                        onClick = if (biometricConfigured) onDisableBiometric else onEnableBiometric,
                        enabled = !busy,
                    )
                } else {
                    SettingsInfo(
                        title = "Биометрия недоступна",
                        subtitle = "Устройство не предоставляет BIOMETRIC_STRONG.",
                    )
                }
                SettingsInfo(
                    title = "Автоблокировка",
                    subtitle = "Через 60 секунд после ухода приложения в фон",
                )
                SettingsInfo(
                    title = "Буфер обмена",
                    subtitle = "Пароли очищаются через 30 секунд",
                )

                SectionTitle("О приложении")
                SettingsInfo(
                    title = "Полностью офлайн",
                    subtitle = "Без INTERNET permission, аккаунтов, облака и телеметрии",
                )
                SettingsInfo(
                    title = "Формат",
                    subtitle = "Vault v2 · Argon2id · AES-256-GCM",
                )

                Button(
                    onClick = onLock,
                    enabled = !busy,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(44.dp),
                    shape = RoundedCornerShape(14.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.primaryContainer,
                        contentColor = MaterialTheme.colorScheme.primary,
                    ),
                ) {
                    Text("Заблокировать хранилище")
                }
            }
        }
    }
}

@Composable
private fun SettingsAction(
    title: String,
    subtitle: String,
    onClick: () -> Unit,
    enabled: Boolean,
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(15.dp))
            .clickable(enabled = enabled, onClick = onClick),
        shape = RoundedCornerShape(15.dp),
        color = MaterialTheme.colorScheme.surfaceVariant,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 11.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleSmall)
                Text(
                    subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Text("›", fontSize = 22.sp, color = MaterialTheme.colorScheme.primary)
        }
    }
}

@Composable
private fun SettingsInfo(title: String, subtitle: String) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(15.dp),
        color = MaterialTheme.colorScheme.surfaceVariant,
    ) {
        Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun SectionTitle(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 2.dp),
    )
}

@Composable
private fun ImportPasswordDialog(
    busy: Boolean,
    onDismiss: () -> Unit,
    onImport: suspend (String) -> String?,
) {
    var password by remember { mutableStateOf("") }
    var showPassword by remember { mutableStateOf(false) }
    var errorText by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    AlertDialog(
        onDismissRequest = { if (!busy) onDismiss() },
        title = { Text("Импорт vault.pnb") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = GeniaGreenSoft,
                ) {
                    Column(modifier = Modifier.padding(10.dp)) {
                        Text(
                            "GeniaPassword 1.0 поддерживает Windows Vault v2",
                            style = MaterialTheme.typography.labelLarge,
                            color = GeniaGreen,
                        )
                        Text(
                            "Argon2id + wrapped VaultKey + AES-GCM",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                Text(
                    "Введите мастер-пароль выбранного хранилища. Перед заменой локального vault будет создан backup.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                CompactField(
                    value = password,
                    onValueChange = {
                        if (it.length <= MAX_MASTER_PASSWORD_CHARS) password = it
                        errorText = null
                    },
                    label = "Мастер-пароль",
                    keyboardType = KeyboardType.Password,
                    visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                    trailing = {
                        TinyTextButton(
                            if (showPassword) "Скрыть" else "Показать",
                            { showPassword = !showPassword },
                        )
                    },
                    error = errorText,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val submittedPassword = password
                    errorText = null
                    scope.launch {
                        val importError = onImport(submittedPassword)
                        errorText = importError
                        if (importError == null) {
                            password = ""
                            showPassword = false
                        }
                    }
                },
                enabled = !busy && password.isNotBlank(),
            ) {
                Text(if (busy) "Импорт…" else "Импортировать")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !busy) { Text("Отмена") }
        },
    )
}

@Composable
private fun CompactField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String?,
    modifier: Modifier = Modifier,
    placeholder: String = "",
    singleLine: Boolean = true,
    keyboardType: KeyboardType = KeyboardType.Text,
    imeAction: ImeAction = ImeAction.Next,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    minHeight: androidx.compose.ui.unit.Dp = 46.dp,
    textAlign: TextAlign = TextAlign.Start,
    error: String? = null,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
) {
    Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        if (!label.isNullOrBlank()) {
            Text(
                label,
                style = MaterialTheme.typography.labelMedium,
                color = if (error == null) {
                    MaterialTheme.colorScheme.onSurfaceVariant
                } else {
                    MaterialTheme.colorScheme.error
                },
            )
        }

        val borderColor = if (error == null) {
            MaterialTheme.colorScheme.outline
        } else {
            MaterialTheme.colorScheme.error
        }

        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .defaultMinSize(minHeight = minHeight),
            shape = RoundedCornerShape(13.dp),
            color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, borderColor),
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(min = minHeight)
                    .padding(
                        horizontal = 12.dp,
                        vertical = if (singleLine) 0.dp else 9.dp,
                    ),
                verticalAlignment = if (singleLine) Alignment.CenterVertically else Alignment.Top,
            ) {
                if (leading != null) {
                    Box(
                        modifier = Modifier.padding(
                            end = 8.dp,
                            top = if (singleLine) 0.dp else 1.dp,
                        ),
                        contentAlignment = Alignment.Center,
                    ) {
                        leading()
                    }
                }

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .then(
                            if (singleLine) {
                                Modifier.heightIn(min = 24.dp)
                            } else {
                                Modifier.heightIn(min = minHeight - 18.dp)
                            },
                        ),
                    contentAlignment = if (singleLine) Alignment.CenterStart else Alignment.TopStart,
                ) {
                    if (value.isEmpty() && placeholder.isNotEmpty()) {
                        Text(
                            placeholder,
                            modifier = if (singleLine) {
                                Modifier
                                    .fillMaxWidth()
                                    .wrapContentHeight(Alignment.CenterVertically)
                            } else {
                                Modifier
                            },
                            style = MaterialTheme.typography.bodyMedium,
                            textAlign = textAlign,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.72f),
                            maxLines = if (singleLine) 1 else Int.MAX_VALUE,
                            overflow = if (singleLine) TextOverflow.Ellipsis else TextOverflow.Clip,
                        )
                    }

                    BasicTextField(
                        value = value,
                        onValueChange = onValueChange,
                        modifier = Modifier
                            .fillMaxWidth()
                            .then(
                                if (singleLine) {
                                    Modifier.wrapContentHeight(Alignment.CenterVertically)
                                } else {
                                    Modifier.heightIn(min = minHeight - 18.dp)
                                },
                            ),
                        singleLine = singleLine,
                        textStyle = TextStyle(
                            color = MaterialTheme.colorScheme.onSurface,
                            fontSize = 15.sp,
                            lineHeight = 20.sp,
                            textAlign = textAlign,
                        ),
                        visualTransformation = visualTransformation,
                        keyboardOptions = KeyboardOptions(
                            keyboardType = keyboardType,
                            imeAction = imeAction,
                        ),
                        cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                    )
                }

                if (trailing != null) {
                    Spacer(Modifier.size(4.dp))
                    Box(contentAlignment = Alignment.Center) {
                        trailing()
                    }
                }
            }
        }

        if (!error.isNullOrBlank()) {
            Text(
                error,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error,
            )
        }
    }
}

@Composable
private fun SmallSoftButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    OutlinedButton(
        onClick = onClick,
        modifier = modifier.height(36.dp),
        shape = RoundedCornerShape(11.dp),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 7.dp),
    ) {
        Text(text, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Clip)
    }
}

@Composable
private fun TinyTextButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    color: Color = MaterialTheme.colorScheme.primary,
) {
    TextButton(
        onClick = onClick,
        modifier = modifier.heightIn(min = 32.dp),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 5.dp, vertical = 2.dp),
    ) {
        Text(
            text,
            color = color,
            fontSize = 12.5.sp,
            lineHeight = 16.sp,
            fontWeight = FontWeight.Medium,
            maxLines = 1,
            softWrap = false,
            overflow = TextOverflow.Clip,
        )
    }
}

@Composable
private fun SecurityNote(title: String, text: String) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = GeniaGreenSoft,
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Surface(
                modifier = Modifier.size(28.dp),
                shape = CircleShape,
                color = GeniaGreen.copy(alpha = 0.13f),
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text("✓", color = GeniaGreen, fontWeight = FontWeight.Bold)
                }
            }
            Spacer(Modifier.size(10.dp))
            Column {
                Text(title, style = MaterialTheme.typography.titleSmall, color = GeniaGreen)
                Text(
                    text,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}


@Composable
private fun EntryActionButton(
    iconRes: Int,
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    contentColor: Color = MaterialTheme.colorScheme.primary,
    containerColor: Color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.62f),
) {
    Surface(
        modifier = modifier
            .height(40.dp)
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(12.dp),
        color = containerColor,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 7.dp),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                modifier = Modifier.size(15.dp),
                tint = contentColor,
            )
            Spacer(Modifier.size(4.dp))
            Text(
                text = text,
                style = MaterialTheme.typography.labelMedium,
                color = contentColor,
                maxLines = 1,
                softWrap = false,
                overflow = TextOverflow.Clip,
            )
        }
    }
}

@Composable
private fun EntryIconButton(
    iconRes: Int,
    contentDescription: String,
    onClick: () -> Unit,
    contentColor: Color = MaterialTheme.colorScheme.onSurfaceVariant,
    containerColor: Color = MaterialTheme.colorScheme.surfaceVariant,
) {
    Surface(
        modifier = Modifier
            .size(40.dp)
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(12.dp),
        color = containerColor,
    ) {
        Box(contentAlignment = Alignment.Center) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = contentDescription,
                modifier = Modifier.size(17.dp),
                tint = contentColor,
            )
        }
    }
}

@Composable
private fun PasswordGeneratorDialog(
    controller: VaultController,
    onDismiss: () -> Unit,
    onUse: (String) -> Unit,
) {
    var length by remember { mutableStateOf(18f) }
    var useUpper by remember { mutableStateOf(true) }
    var useDigits by remember { mutableStateOf(true) }
    var useSymbols by remember { mutableStateOf(true) }
    var excludeAmbiguous by remember { mutableStateOf(true) }
    var generated by remember { mutableStateOf(controller.generatePassword(18, true, true, true, true)) }

    fun regenerate() {
        generated = controller.generatePassword(
            length = length.toInt(),
            useUpper = useUpper,
            useDigits = useDigits,
            useSymbols = useSymbols,
            excludeAmbiguous = excludeAmbiguous,
        )
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Генератор пароля") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(14.dp),
                    color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.62f),
                ) {
                    Column(modifier = Modifier.padding(12.dp)) {
                        Text(
                            generated,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurface,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            "${generated.length} символов · генерируется локально",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text("Длина", style = MaterialTheme.typography.titleSmall)
                    Surface(
                        shape = RoundedCornerShape(999.dp),
                        color = MaterialTheme.colorScheme.surfaceVariant,
                    ) {
                        Text(
                            length.toInt().toString(),
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                            style = MaterialTheme.typography.labelLarge,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                }
                Slider(
                    value = length,
                    onValueChange = { length = it },
                    onValueChangeFinished = { regenerate() },
                    valueRange = 8f..64f,
                    steps = 55,
                )

                GeneratorOption("Заглавные A–Z", useUpper) {
                    useUpper = it
                    regenerate()
                }
                GeneratorOption("Цифры 0–9", useDigits) {
                    useDigits = it
                    regenerate()
                }
                GeneratorOption("Спецсимволы", useSymbols) {
                    useSymbols = it
                    regenerate()
                }
                GeneratorOption("Исключить похожие O/0, l/1", excludeAmbiguous) {
                    excludeAmbiguous = it
                    regenerate()
                }

                OutlinedButton(
                    onClick = { regenerate() },
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(12.dp),
                ) {
                    Text("Сгенерировать ещё")
                }
            }
        },
        confirmButton = {
            Button(onClick = { onUse(generated) }) { Text("Использовать") }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("Отмена") }
        },
    )
}

@Composable
private fun GeneratorOption(
    title: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable { onCheckedChange(!checked) }
            .padding(horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Checkbox(checked = checked, onCheckedChange = onCheckedChange)
        Text(title, style = MaterialTheme.typography.bodyMedium)
    }
}

private data class AuditResult(
    val weak: List<VaultEntry>,
    val repeated: List<VaultEntry>,
    val old: List<VaultEntry>,
    val incomplete: List<VaultEntry>,
)

private fun auditEntries(entries: List<VaultEntry>): AuditResult {
    val passwordGroups = entries
        .filter { it.password.isNotBlank() }
        .groupBy { it.password }
    val repeatedPasswords = passwordGroups.filterValues { it.size > 1 }.keys

    fun passwordClassCount(value: String): Int = listOf(
        value.any(Char::isLowerCase),
        value.any(Char::isUpperCase),
        value.any(Char::isDigit),
        value.any { !it.isLetterOrDigit() },
    ).count { it }

    val weak = entries.filter { entry ->
        val password = entry.password
        password.isNotBlank() && (password.length < 12 || passwordClassCount(password) < 3)
    }
    val repeated = entries.filter { it.password.isNotBlank() && it.password in repeatedPasswords }
    val old = entries.filter { entry ->
        val changed = runCatching { Instant.parse(entry.passwordUpdatedUtc) }.getOrNull() ?: return@filter false
        Duration.between(changed, Instant.now()).toDays() >= 180
    }
    val incomplete = entries.filter { it.userName.isBlank() || it.website.isBlank() }
    return AuditResult(weak, repeated, old, incomplete)
}

@Composable
private fun SecurityAuditDialog(
    entries: List<VaultEntry>,
    onDismiss: () -> Unit,
) {
    val result = remember(entries) { auditEntries(entries) }
    val issueCount = result.weak.size + result.repeated.size + result.old.size

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp, vertical = 20.dp)
                .heightIn(max = 760.dp),
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surface,
        ) {
            Column(modifier = Modifier.fillMaxWidth()) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text("Аудит безопасности", style = MaterialTheme.typography.titleLarge)
                        Text(
                            "Проверка выполняется только на устройстве",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    TinyTextButton("Закрыть", onDismiss)
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outline.copy(alpha = 0.65f))

                Column(
                    modifier = Modifier
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Surface(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(18.dp),
                        color = if (issueCount == 0) GeniaGreenSoft else MaterialTheme.colorScheme.primaryContainer,
                    ) {
                        Column(modifier = Modifier.padding(14.dp)) {
                            Text(
                                if (issueCount == 0) "Критичных замечаний нет" else "Найдено замечаний: $issueCount",
                                style = MaterialTheme.typography.titleMedium,
                                color = if (issueCount == 0) GeniaGreen else MaterialTheme.colorScheme.primary,
                            )
                            Text(
                                "Проверено ${entries.size} записей. Пароли никуда не передаются.",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }

                    AuditIssueCard(
                        title = "Слабые пароли",
                        subtitle = "Короче 12 символов или недостаточно разных групп символов",
                        entries = result.weak,
                    )
                    AuditIssueCard(
                        title = "Повторяющиеся пароли",
                        subtitle = "Один и тот же пароль используется в нескольких записях",
                        entries = result.repeated,
                    )
                    AuditIssueCard(
                        title = "Давно не менялись",
                        subtitle = "Пароль не менялся 180 дней или дольше",
                        entries = result.old,
                    )
                    AuditIssueCard(
                        title = "Неполные записи",
                        subtitle = "Не заполнен логин или сайт — это не уязвимость, а подсказка по порядку",
                        entries = result.incomplete,
                        informational = true,
                    )
                }
            }
        }
    }
}

@Composable
private fun AuditIssueCard(
    title: String,
    subtitle: String,
    entries: List<VaultEntry>,
    informational: Boolean = false,
) {
    val ok = entries.isEmpty()
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        color = when {
            ok -> GeniaGreenSoft
            informational -> MaterialTheme.colorScheme.surfaceVariant
            else -> GeniaRedSoft
        },
    ) {
        Column(
            modifier = Modifier.padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(title, style = MaterialTheme.typography.titleSmall)
                Text(
                    if (ok) "✓" else entries.size.toString(),
                    style = MaterialTheme.typography.labelLarge,
                    color = when {
                        ok -> GeniaGreen
                        informational -> MaterialTheme.colorScheme.primary
                        else -> MaterialTheme.colorScheme.error
                    },
                )
            }
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (!ok) {
                Text(
                    entries.take(5).joinToString(" · ") { it.title.ifBlank { "Без названия" } } +
                        if (entries.size > 5) " · ещё ${entries.size - 5}" else "",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

private fun validateEntryInput(
    title: String,
    website: String,
    userName: String,
    password: String,
    notes: String,
    fields: List<CustomField>,
): String? = when {
    title.isBlank() -> "Введите название записи."
    title.length > VaultConstraints.MAX_TITLE_CHARS -> "Название слишком длинное."
    website.length > VaultConstraints.MAX_WEBSITE_CHARS -> "Адрес сайта слишком длинный."
    userName.length > VaultConstraints.MAX_USERNAME_CHARS -> "Логин слишком длинный."
    password.length > VaultConstraints.MAX_PASSWORD_CHARS -> "Пароль слишком длинный."
    notes.length > VaultConstraints.MAX_NOTES_CHARS -> "Заметки слишком длинные."
    fields.size > VaultConstraints.MAX_CUSTOM_FIELDS_PER_ENTRY -> "Слишком много дополнительных полей."
    fields.any { it.name.length > VaultConstraints.MAX_FIELD_NAME_CHARS } -> "Название дополнительного поля слишком длинное."
    fields.any { it.value.length > VaultConstraints.MAX_FIELD_VALUE_CHARS } -> "Значение дополнительного поля слишком длинное."
    fields.any { it.name.isBlank() && it.value.isNotBlank() } -> "Укажите название дополнительного поля."
    else -> null
}
