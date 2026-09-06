<template>
    <div>
        <p v-if="!loading && !entries.length" class="text-muted text-sm">
            {{ mangaId ? 'No active downloads for this series.' : 'Queue is empty.' }}
        </p>
        <div v-else class="overflow-x-auto">
            <table class="arr-table">
                <thead>
                    <tr>
                        <th v-if="!mangaId"></th>
                        <th v-if="!mangaId">Series</th>
                        <th>Issue</th>
                        <th>Release</th>
                        <th>Indexer</th>
                        <th>Client</th>
                        <th>Status</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="e in entries" :key="e.jobId">
                        <td v-if="!mangaId">
                            <img :src="`/v2/Manga/${e.mangaId}/Cover/Medium`" alt="" class="w-8 h-11 object-cover rounded" loading="lazy" />
                        </td>
                        <td v-if="!mangaId">
                            <NuxtLink :to="`/manga/${e.mangaId}`" class="font-medium hover:underline">{{ e.mangaName }}</NuxtLink>
                        </td>
                        <td class="tabular-nums">#{{ e.chapterNumber }}</td>
                        <td class="text-sm max-w-72 truncate" :title="e.releaseTitle">{{ e.releaseTitle }}</td>
                        <td class="text-sm text-muted">{{ e.indexerName }}</td>
                        <td>
                            <UBadge size="sm" variant="subtle" :color="e.protocol === 'Usenet' ? 'secondary' : 'primary'">
                                {{ e.clientName === 'QBittorrent' ? 'qBittorrent' : 'SABnzbd' }}
                            </UBadge>
                        </td>
                        <td class="min-w-36">
                            <div class="flex items-center gap-2">
                                <UBadge size="sm" variant="subtle" :color="statusColor(e.status)">{{ e.status }}</UBadge>
                                <div v-if="e.status === 'Downloading'" class="arr-progress grow">
                                    <div class="arr-progress__fill" :style="{ width: `${Math.round(e.progress * 100)}%` }" />
                                </div>
                            </div>
                            <p v-if="e.errorMessage" class="text-error text-xs mt-1 truncate" :title="e.errorMessage">{{ e.errorMessage }}</p>
                        </td>
                        <td>
                            <UTooltip text="Remove from queue (does not cancel the download in qBittorrent/SABnzbd)">
                                <UButton
                                    size="xs"
                                    variant="ghost"
                                    color="warning"
                                    icon="i-lucide-x"
                                    :loading="removing === e.jobId"
                                    @click="remove(e.jobId)" />
                            </UTooltip>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>
    </div>
</template>

<script setup lang="ts">
type ComicQueueEntry = {
    jobId: string;
    chapterId: string;
    mangaId: string;
    mangaName: string;
    chapterNumber: string;
    releaseTitle: string;
    indexerName: string;
    protocol: 'Torrent' | 'Usenet';
    clientName: 'QBittorrent' | 'Sabnzbd';
    status: 'Queued' | 'Downloading' | 'Importing' | 'Imported' | 'Failed';
    progress: number;
    errorMessage?: string | null;
    createdAt: string;
};

const props = defineProps<{ mangaId?: string; pollIntervalMs?: number }>();
const entries = ref<ComicQueueEntry[]>([]);
const loading = ref(true);
const removing = ref('');
let timer: ReturnType<typeof setInterval> | undefined;

const statusColor = (status: ComicQueueEntry['status']) => {
    switch (status) {
        case 'Imported':
            return 'success';
        case 'Failed':
            return 'error';
        case 'Downloading':
        case 'Importing':
            return 'primary';
        default:
            return 'neutral';
    }
};

const load = async () => {
    try {
        const params = new URLSearchParams();
        if (props.mangaId) params.set('mangaId', props.mangaId);
        entries.value = await $fetch<ComicQueueEntry[]>(`/v2/Comic/Queue?${params.toString()}`);
    } catch {
        // keep whatever was last shown rather than flashing empty on a transient failure
    } finally {
        loading.value = false;
    }
};

const remove = async (jobId: string) => {
    removing.value = jobId;
    try {
        await $fetch(`/v2/Comic/Queue/${encodeURIComponent(jobId)}`, { method: 'DELETE' });
        await load();
    } finally {
        removing.value = '';
    }
};

onMounted(() => {
    load();
    timer = setInterval(load, props.pollIntervalMs ?? 10000);
});
onUnmounted(() => {
    if (timer) clearInterval(timer);
});

defineExpose({ load });
</script>
