
const permittedSlugString = "abcdefghijklmnopqrstuvwxyz0123456789.-_";
const permittedSlugChars = permittedSlugString.split('');

document.querySelectorAll('form.single-submit').forEach(form => {
    form.addEventListener('submit', function(event) {
        if (this.dataset.submitted && this.dataset.submitted === "submitted") {
            event.preventDefault();
            return;
        }
        this.dataset.submitted = "submitted";
        this.querySelectorAll('button, input[type="submit"]').forEach(button => {
            button.disabled = true;
        });
        // Not all submit buttons are descendants of their forms
        if(event.submitter){
            event.submitter.disabled = true;
        }
    });
});
