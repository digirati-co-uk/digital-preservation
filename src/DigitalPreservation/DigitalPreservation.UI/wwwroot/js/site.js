
const permittedSlugString = "abcdefghijklmnopqrstuvwxyz0123456789.-_";
const permittedSlugChars = permittedSlugString.split('');

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


function openHtmlStringInNewTab(htmlString) {
    const newWindow = window.open('', '_blank');
    const raw = decodeURIComponent(atob(htmlString).split('').map(function (c) {
        return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
    }).join(''));
    newWindow.document.write(raw);
    newWindow.document.close();
};
