/* ═══ AI Document Organizer — JavaScript ═══ */

// ─── Sidebar Mobile Toggle ───
document.addEventListener('DOMContentLoaded', () => {
    const sidebar  = document.getElementById('sidebar');
    const overlay  = document.getElementById('sidebarOverlay');
    const openBtn  = document.getElementById('sidebarOpen');
    const closeBtn = document.getElementById('sidebarClose');

    function openSidebar() {
        sidebar?.classList.add('open');
        overlay?.classList.add('show');
        document.body.style.overflow = 'hidden';
    }
    function closeSidebar() {
        sidebar?.classList.remove('open');
        overlay?.classList.remove('show');
        document.body.style.overflow = '';
    }

    openBtn?.addEventListener('click', openSidebar);
    closeBtn?.addEventListener('click', closeSidebar);
    overlay?.addEventListener('click', closeSidebar);

    // ─── Auto-dismiss toasts ───
    document.querySelectorAll('.toast-msg').forEach(toast => {
        setTimeout(() => { toast.style.opacity = '0'; toast.style.transform = 'translateX(40px)'; toast.style.transition = '0.4s'; setTimeout(() => toast.remove(), 400); }, 4500);
        toast.querySelector('.toast-close')?.addEventListener('click', () => toast.remove());
    });

    // ─── Drop Zone ───
    initDropZone();

    // ─── File Preview ───
    initPreviewModal();

    // ─── AI Chat ───
    initAiChat();
});

// ─── Drag & Drop Upload ───
function initDropZone() {
    const zone    = document.getElementById('dropZone');
    const input   = document.getElementById('fileInput');
    const preview = document.getElementById('filePreview');
    const fname   = document.getElementById('fileName');
    const fsize   = document.getElementById('fileSize');

    if (!zone) return;

    zone.addEventListener('click', () => input?.click());

    zone.addEventListener('dragover', e => { e.preventDefault(); zone.classList.add('drag-over'); });
    zone.addEventListener('dragleave', e => { if (!zone.contains(e.relatedTarget)) zone.classList.remove('drag-over'); });
    zone.addEventListener('drop', e => {
        e.preventDefault();
        zone.classList.remove('drag-over');
        const file = e.dataTransfer?.files[0];
        if (file) setFile(file);
    });

    input?.addEventListener('change', () => {
        if (input.files?.[0]) setFile(input.files[0]);
    });

    function setFile(file) {
        if (!input) return;
        const dt = new DataTransfer();
        dt.items.add(file);
        input.files = dt.files;

        if (fname) fname.textContent = file.name;
        if (fsize) fsize.textContent = formatBytes(file.size);
        preview?.classList.remove('d-none');
        zone.classList.add('has-file');
    }
}

function formatBytes(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(2) + ' MB';
}

// ─── Preview Modal (PDF / Image) ───
function initPreviewModal() {
    const modal = document.getElementById('previewModal');
    if (!modal) return;
    const frame = document.getElementById('previewFrame');
    const img   = document.getElementById('previewImg');

    document.querySelectorAll('[data-preview-url]').forEach(btn => {
        btn.addEventListener('click', e => {
            e.preventDefault();
            const url  = btn.dataset.previewUrl;
            const type = btn.dataset.previewType || '';

            if (frame) frame.src = '';
            if (img)   img.src  = '';

            if (type.includes('pdf')) {
                if (frame) { frame.classList.remove('d-none'); frame.src = url; }
                if (img)   img.classList.add('d-none');
            } else {
                if (img)   { img.classList.remove('d-none'); img.src = url; }
                if (frame) frame.classList.add('d-none');
            }

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        });
    });
}

// ─── AI Chat ───
function initAiChat() {
    const form    = document.getElementById('chatForm');
    const input   = document.getElementById('chatInput');
    const msgs    = document.getElementById('chatMessages');
    const token   = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!form) return;

    form.addEventListener('submit', async e => {
        e.preventDefault();
        const message = input?.value.trim();
        if (!message) return;

        appendBubble('user', message);
        input.value = '';
        input.style.height = 'auto';

        const typingId = appendTyping();

        try {
            const res = await fetch('/AiAssistant/Chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token ?? '' },
                body: JSON.stringify({ message })
            });
            const data = await res.json();
            removeTyping(typingId);
            appendBubble('ai', data.success ? data.response : (data.error || 'An error occurred.'));
        } catch {
            removeTyping(typingId);
            appendBubble('ai', 'Failed to connect to AI assistant. Please try again.');
        }
    });

    // Auto-resize textarea
    input?.addEventListener('input', () => {
        input.style.height = 'auto';
        input.style.height = Math.min(input.scrollHeight, 120) + 'px';
    });

    // Enter to submit, Shift+Enter for newline
    input?.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
    });

    function appendBubble(role, text) {
        if (!msgs) return;
        const div = document.createElement('div');
        div.className = `chat-bubble ${role}`;
        const avatarIcon = role === 'ai' ? '<i class="bi bi-robot"></i>' : '<i class="bi bi-person"></i>';
        div.innerHTML = `
            <div class="bubble-avatar ${role}">${avatarIcon}</div>
            <div class="bubble-text">${escapeHtml(text).replace(/\n/g, '<br>')}</div>`;
        msgs.appendChild(div);
        msgs.scrollTop = msgs.scrollHeight;
    }

    function appendTyping() {
        if (!msgs) return null;
        const id = 'typing-' + Date.now();
        const div = document.createElement('div');
        div.className = 'chat-bubble ai'; div.id = id;
        div.innerHTML = `<div class="bubble-avatar ai"><i class="bi bi-robot"></i></div>
            <div class="typing-indicator"><div class="typing-dot"></div><div class="typing-dot"></div><div class="typing-dot"></div></div>`;
        msgs.appendChild(div);
        msgs.scrollTop = msgs.scrollHeight;
        return id;
    }

    function removeTyping(id) {
        if (id) document.getElementById(id)?.remove();
    }

    function escapeHtml(str) {
        return str.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
}

// ─── Delete Confirmation ───
function confirmDelete(formId, name) {
    if (confirm(`Are you sure you want to permanently delete "${name}"?\n\nThis action cannot be undone.`)) {
        document.getElementById(formId)?.submit();
    }
}

// ─── Mark Processing Status Refresh ───
function pollProcessingStatus(docId, statusBadgeId) {
    const badge = document.getElementById(statusBadgeId);
    if (!badge) return;

    const interval = setInterval(async () => {
        try {
            const res = await fetch(`/Documents/ProcessingStatus/${docId}`);
            if (res.ok) {
                const data = await res.json();
                if (data.status !== 'Processing' && data.status !== 'Pending') {
                    clearInterval(interval);
                    location.reload();
                }
            }
        } catch { /* silent */ }
    }, 3000);

    // Stop polling after 2 minutes
    setTimeout(() => clearInterval(interval), 120000);
}

// ─── Dashboard mini-bar charts ───
function drawCategoryBars(data) {
    const container = document.getElementById('categoryBars');
    if (!container || !data?.length) return;

    const max = Math.max(...data.map(d => d.count), 1);
    container.innerHTML = data.map(d => `
        <div class="d-flex align-items-center gap-3 mb-2">
            <div style="width:130px; font-size:13px; color:var(--text-secondary); text-overflow:ellipsis; overflow:hidden; white-space:nowrap;">${escapeHtml(d.name)}</div>
            <div style="flex:1; height:8px; background:rgba(255,255,255,0.06); border-radius:4px; overflow:hidden;">
                <div style="height:100%; width:${Math.round(d.count/max*100)}%; background:${d.color}; border-radius:4px; transition:width 0.6s ease;"></div>
            </div>
            <div style="width:28px; text-align:right; font-size:13px; font-weight:600; color:var(--text-primary);">${d.count}</div>
        </div>`).join('');

    function escapeHtml(s) { return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
}
