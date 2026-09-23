# Vault compatibility

The regular Windows and Android editions use the same **Vault v2** cryptographic container.

The intended workflow between devices is file based:

1. export or copy the current vault;
2. keep a backup of the previous file;
3. import the new vault on the target device;
4. unlock it and verify several records before replacing other backups.

The regular editions do not depend on a cloud service or online synchronization.

PRO editions may contain additional encrypted metadata such as categories or favorites and are maintained separately. Do not rely on the regular edition to preserve future or PRO-only fields unless that compatibility has been explicitly tested for the specific release.
