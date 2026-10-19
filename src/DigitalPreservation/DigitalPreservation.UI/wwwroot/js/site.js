
const permittedSlugString = "abcdefghijklmnopqrstuvwxyz0123456789.-_";
const permittedSlugChars = permittedSlugString.split('');

// Keeps --dlip-header-height (site.css) equal to the sticky header's real height, so
// scroll-padding-top never leaves a keyboard-focused control behind it (WCAG 2.4.11). The
// header isn't a fixed height: it grows when it wraps below Bootstrap's md breakpoint - a
// narrow window, or a desktop browser zoomed in - or when a long signed-in name wraps in the
// brand column (review on #384). Runs on every page, since the header is in the shared layout.
const stickyHeader = document.querySelector('header.sticky-top');
if (stickyHeader) {
    new ResizeObserver(() => document.documentElement.style.setProperty(
        '--dlip-header-height', stickyHeader.offsetHeight + 'px')).observe(stickyHeader);
}

document.querySelectorAll('form.single-submit').forEach(form => {
    form.addEventListener('submit', function(event) {
        if (this.dataset.submitted && this.dataset.submitted === "submitted") {
            event.preventDefault();
            return;
        }
        this.dataset.submitted = "submitted";
        
        // we don't want to disable the buttons, because that would prevent any value=x they have being submitted
        // But we can visually indicate it is disabled with CSS (and aria-disabled)
        this.querySelectorAll('button, input[type="submit"]').forEach(button => {
            button.classList.add("disabled");
            button.setAttribute('aria-disabled', 'true');
        });
        // Not all submit buttons are descendants of their forms
        if(event.submitter){
            event.submitter.classList.add("disabled");
            event.submitter.setAttribute('aria-disabled', 'true');
        }
    });
});
