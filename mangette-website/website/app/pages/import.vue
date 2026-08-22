<template>
    <UPage>
        <UPageHeader
            title="Library Import"
            description="Scan your manga folder, match series, and import without re-downloading. Exact (100%) matches import automatically." />
        <UPageBody>
            <div class="flex flex-wrap items-center gap-2 mb-4">
                <UButton icon="i-lucide-folder-search" :loading="scanning" @click="scan">Scan folders</UButton>
                <UButton
                    icon="i-lucide-search"
                    variant="outline"
                    :disabled="!rows.length || matching"
                    :loading="matching"
                    @click="matchAll">
                    Match all
                </UButton>
                <UButton
                    icon="i-lucide-download"
                    :disabled="!importable.length || importing"
                    :loading="importing"
                    @click="importHighConfidence">
                    Import matches ≥ 90%
                </UButton>
                <p v-if="scanResult" class="text-muted text-sm">
                    Scanning <code>{{ scanResult.basePath }}</code> — {{ scanResult.unmappedFolders.length }} to import,
                    {{ scanResult.mappedFolderCount }} already in library, {{ scanResult.totalFoldersSeen }} folders seen.
                </p>
            </div>
            <p v-if="message" class="text-sm mb-3" :class="error ? 'text-error' : 'text-muted'">{{ message }}</p>
            <UAlert
                v-if="scanResult?.warning"
                color="warning"
                :title="scanResult.warning"
                icon="i-lucide-triangle-alert"
                class="mb-3" />
            <UAlert
                v-if="!scanning && scanResult && rows.length === 0 && !scanResult.warning"
                title="Nothing new to import"
                description="Every series folder is already in Mangette, or the library path is empty. Set the library folder in Settings if this looks wrong."
                icon="i-lucide-circle-check" />
            <div class="flex flex-col gap-2">
                <div
                    v-for="row in rows"
                    :key="row.folderName"
                    class="flex max-lg:flex-col flex-row gap-3 items-stretch lg:items-center bg-elevated rounded-lg p-3">
                    <div class="lg:w-1/4 min-w-0">
                        <p class="font-medium truncate">{{ row.folderName }}</p>
                        <p class="text-muted text-xs">{{ row.archiveCount }} files · search “{{ row.suggestedQuery }}”</p>
                    </div>
                    <div class="grow min-w-0">
                        <p v-if="row.imported" class="text-success text-sm">Imported: {{ row.importedName }}</p>
                        <template v-else>
                            <USelect
                                v-if="row.matches.length"
                                v-model="row.selected"
                                :items="row.matches.map((m) => ({ label: `${m.score}% · ${m.name} (${m.connectorName})`, value: keyOf(m) }))"
                                class="w-full" />
                            <p v-else-if="row.matching" class="text-muted text-sm">Matching…</p>
                            <p v-else-if="row.importing" class="text-muted text-sm">Importing exact match…</p>
                            <p v-else class="text-muted text-sm">Not matched yet</p>
                            <p v-if="row.error" class="text-error text-sm mt-1">{{ row.error }}</p>
                        </template>
                    </div>
                    <div class="flex gap-2 shrink-0">
                        <UButton size="sm" variant="outline" :loading="row.matching" :disabled="row.imported" @click="matchOne(row)">
                            Match
                        </UButton>
                        <UButton size="sm" :disabled="!row.selected || row.imported" :loading="row.importing" @click="importOne(row)">
                            Import
                        </UButton>
                    </div>
                </div>
            </div>
        </UPageBody>
    </UPage>
</template>

<script setup lang="ts">
type Candidate = {
    name: string;
    connectorName: string;
    idOnSite: string;
    websiteUrl?: string | null;
    coverUrl: string;
    score: number;
};
type Row = {
    folderName: string;
    archiveCount: number;
    suggestedQuery: string;
    matches: Candidate[];
    selected?: string;
    matching: boolean;
    importing: boolean;
    imported: boolean;
    importedName?: string;
    error?: string;
};

const apiError = (e: unknown) => {
    if (typeof e === 'object' && e) {
        const x = e as { data?: unknown; message?: string; statusMessage?: string };
        const d = x.data;
        if (typeof d === 'string' && d.trim()) return d.trim();
        if (d && typeof d === 'object') {
            const o = d as Record<string, unknown>;
            for (const k of ['detail', 'title', 'message']) {
                if (typeof o[k] === 'string' && (o[k] as string).trim()) return (o[k] as string).trim();
            }
        }
        if (typeof x.statusMessage === 'string' && x.statusMessage.trim()) return x.statusMessage.trim();
        if (typeof x.message === 'string' && x.message.trim()) return x.message.trim();
    }
    return '';
};

const { $api } = useNuxtApp();
const scanning = ref(false);
const matching = ref(false);
const importing = ref(false);
const message = ref('');
const error = ref(false);
const scanResult = ref<{
    libraryId: string;
    libraryName: string;
    basePath: string;
    unmappedFolders: Row[];
    mappedFolderCount: number;
    warning?: string | null;
    totalFoldersSeen: number;
} | null>(null);
const rows = ref<Row[]>([]);

const keyOf = (m: Candidate) => `${m.connectorName}::${m.idOnSite}`;
const candidateFromKey = (row: Row): Candidate | undefined => row.matches.find((m) => keyOf(m) === row.selected);
const importable = computed(() => rows.value.filter((r) => !r.imported && candidateFromKey(r) && (candidateFromKey(r)?.score ?? 0) >= 90));

const scan = async () => {
    scanning.value = true;
    error.value = false;
    message.value = '';
    try {
        const data = await $api('/v2/LibraryImport/Scan');
        scanResult.value = data as typeof scanResult.value;
        rows.value = (data?.unmappedFolders ?? []).map((f) => ({
            folderName: f.folderName,
            archiveCount: f.archiveCount,
            suggestedQuery: f.suggestedQuery,
            matches: [],
            matching: false,
            importing: false,
            imported: false,
            error: '',
        }));
        if (!rows.value.length)
            message.value = data?.mappedFolderCount ? 'All folders are already in the library.' : 'No series folders found in the library path.';
    } catch (e: unknown) {
        error.value = true;
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        message.value = body || 'Could not scan the library folder. Check Settings → Library folder. The Windows service cannot see mapped drives (Z:\\); use D:\\Manga or \\\\server\\share\\Manga.';
    } finally {
        scanning.value = false;
    }
};

const matchOne = async (row: Row) => {
    row.matching = true;
    row.error = '';
    try {
        const result = await $api('/v2/LibraryImport/Match', {
            method: 'POST',
            body: { folderName: row.folderName, query: row.suggestedQuery },
        });
        row.matches = result?.matches ?? [];
        const best = row.matches[0];
        row.selected = best ? keyOf(best) : undefined;
        if (!row.matches.length) {
            row.error = `No site match for “${row.folderName}”.`;
            return;
        }
        if (best && best.score >= 100) {
            row.matching = false;
            await importOne(row);
        }
    } catch (e: unknown) {
        error.value = true;
        row.error = apiError(e) || `Match failed for “${row.folderName}”.`;
        message.value = row.error;
    } finally {
        row.matching = false;
    }
};

const matchAll = async () => {
    matching.value = true;
    for (const row of rows.value) {
        if (!row.imported && !row.matches.length) await matchOne(row);
    }
    matching.value = false;
    const imported = rows.value.filter((r) => r.imported).length;
    const failed = rows.value.filter((r) => r.error && !r.imported).length;
    if (imported || failed) {
        error.value = failed > 0;
        message.value = failed
            ? `Auto-imported ${imported} exact matches, ${failed} failed. Red text is the reason; also data/logs/mangette-errors.log`
            : `Auto-imported ${imported} exact (100%) matches.`;
    }
};

const importOne = async (row: Row) => {
    const pick = candidateFromKey(row);
    if (!pick || !scanResult.value) return;
    row.importing = true;
    try {
        const result = await $api('/v2/LibraryImport/Import', {
            method: 'POST',
            body: {
                libraryId: scanResult.value.libraryId,
                folderName: row.folderName,
                connectorName: pick.connectorName,
                idOnSite: pick.idOnSite,
            },
        });
        row.imported = true;
        row.importedName = result?.name ?? pick.name;
        row.error = '';
        await refreshNuxtData(FetchKeys.Manga.All);
    } catch (e: unknown) {
        error.value = true;
        row.error = apiError(e) || `Import failed for “${row.folderName}”. See data/logs/mangette-errors.log`;
        message.value = row.error;
    } finally {
        row.importing = false;
    }
};

const importHighConfidence = async () => {
    importing.value = true;
    let ok = 0;
    let fail = 0;
    for (const row of importable.value) {
        await importOne(row);
        if (row.imported) ok++;
        else fail++;
    }
    importing.value = false;
    message.value =
        fail > 0
            ? `Imported ${ok}, failed ${fail}. Open the red row for the reason, or data/logs/mangette-errors.log`
            : `Imported ${ok} high-confidence matches. Existing .cbz files will be marked downloaded.`;
};

onMounted(() => scan());
useHead({ title: 'Library Import' });
</script>
