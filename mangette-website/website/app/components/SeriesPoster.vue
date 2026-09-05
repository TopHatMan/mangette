<template>
    <button type="button" class="series-poster group text-left" @click="$emit('click')">
        <div class="series-poster__art">
            <img v-if="!broken" :src="src" alt="" class="h-full w-full object-cover" loading="lazy" decoding="async" @error="broken = true" />
            <div class="series-poster__shade" />
            <p class="series-poster__title">{{ title }}</p>
            <div v-if="chapterCount != null" class="series-poster__bar">
                <div class="series-poster__bar-fill" :style="{ width: `${progress}%` }" />
            </div>
            <span v-if="badge" class="series-poster__badge">{{ badge }}</span>
            <span
                v-if="showMonitor !== false"
                class="series-poster__monitor"
                :class="{ 'series-poster__monitor--on': monitored }"
                :title="monitored ? 'Unmonitor' : 'Monitor'"
                @click.stop="$emit('toggleMonitor', !monitored)">
                <UIcon :name="monitored ? 'i-lucide-bookmark-check' : 'i-lucide-bookmark'" class="size-4" />
            </span>
        </div>
        <p v-if="subtitle" class="series-poster__sub">{{ subtitle }}</p>
    </button>
</template>

<script setup lang="ts">
const props = defineProps<{
    title: string;
    src: string;
    year?: number | null;
    chapterCount?: number | null;
    downloadedCount?: number | null;
    badge?: string;
    subtitle?: string;
    monitored?: boolean;
    showMonitor?: boolean;
}>();

defineEmits<{ click: []; toggleMonitor: [monitored: boolean] }>();

const broken = ref(false);
const progress = computed(() => {
    const total = props.chapterCount ?? 0;
    if (total <= 0) return 0;
    return Math.min(100, Math.round(((props.downloadedCount ?? 0) / total) * 100));
});
</script>
