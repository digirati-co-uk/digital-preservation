class Result:

    def __init__(self, success:bool):
        self.success = success
        self.failure = not success
        self.value = None
        self.error = None